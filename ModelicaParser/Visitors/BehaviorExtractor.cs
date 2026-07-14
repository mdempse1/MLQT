using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// Extracts the top-level behaviour a class declares in its own body: the equations, connect()
/// statements and algorithm statements directly in its equation/algorithm sections (not those nested
/// inside if/for/when, which are part of an outer equation, and not inherited behaviour). Text is sliced
/// verbatim from the source. Only the outermost class is examined.
/// </summary>
public static class BehaviorExtractor
{
    public static ClassBehavior ExtractFromCode(string classCode)
    {
        // Parse normalizes line endings internally, so the parse-tree offsets index LF-normalized text.
        // Normalize here too, so the source we slice (below) uses the same offsets — otherwise CRLF input
        // shifts every slice by the number of stripped '\r' characters.
        classCode = ModelicaParserHelper.NormalizeLineEndings(classCode);
        var composition = ModelicaParserHelper.Parse(classCode)
            ?.class_definition()?.FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();
        if (composition?.children is null)
            return ClassBehavior.Empty;

        var equations = new List<BehaviorLine>();
        var connections = new List<ConnectionPair>();
        var statements = new List<BehaviorLine>();
        var hasEquation = false;
        var hasAlgorithm = false;

        foreach (var child in composition.children)
        {
            switch (child)
            {
                case modelicaParser.Equation_sectionContext eq:
                    hasEquation = true;
                    List<string>? eqPending = null;
                    foreach (var eoc in eq.equation_or_comment())
                    {
                        if (eoc.c_comment() is { } eqComment)
                        {
                            (eqPending ??= new List<string>()).Add(eqComment.GetText().Trim());
                            continue;
                        }
                        var equation = eoc.equation();
                        if (equation is null)
                            continue;
                        if (equation.connect_clause() is { } connect && connect.component_reference().Length >= 2)
                        {
                            var refs = connect.component_reference();
                            connections.Add(new ConnectionPair(refs[0].GetText(), refs[1].GetText()));
                        }
                        else
                        {
                            equations.Add(new BehaviorLine(
                                Slice(classCode, equation.Start.StartIndex, equation.Stop.StopIndex),
                                eqPending ?? (IReadOnlyList<string>)Array.Empty<string>()));
                        }
                        eqPending = null;
                    }
                    break;

                case modelicaParser.Algorithm_sectionContext alg:
                    hasAlgorithm = true;
                    List<string>? algPending = null;
                    foreach (var soc in alg.statement_or_comment())
                    {
                        if (soc.c_comment() is { } algComment)
                        {
                            (algPending ??= new List<string>()).Add(algComment.GetText().Trim());
                            continue;
                        }
                        var statement = soc.statement();
                        if (statement is not null)
                            statements.Add(new BehaviorLine(
                                Slice(classCode, statement.Start.StartIndex, statement.Stop.StopIndex),
                                algPending ?? (IReadOnlyList<string>)Array.Empty<string>()));
                        algPending = null;
                    }
                    break;
            }
        }

        return new ClassBehavior(equations, connections, statements, hasEquation, hasAlgorithm);
    }

    private static string Slice(string code, int start, int stop)
        => start >= 0 && stop >= start && stop < code.Length ? code[start..(stop + 1)] : string.Empty;
}
