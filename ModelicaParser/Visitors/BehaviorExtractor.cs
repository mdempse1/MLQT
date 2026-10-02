using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// Extracts the behaviour a class declares in its own body: the equations and algorithm statements
/// directly in its equation/algorithm sections (one nested inside if/for/when is part of the outer
/// equation's text), and every connect() from <see cref="ConnectClauses"/>, nested ones included - a
/// connect in a for loop is how an array of components is wired, and is still a connection. Not
/// inherited behaviour. Text is sliced verbatim from the source. Only the outermost class is examined.
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
                        // Comment-only when there is no equation: an equation may carry comments
                        // before its ';' too (B432), and those are not the next equation's.
                        if (eoc.equation() is null && eoc.c_comment() is { } eqComments)
                        {
                            foreach (var eqComment in eqComments)
                                (eqPending ??= new List<string>()).Add(eqComment.GetText().Trim());
                            continue;
                        }
                        var equation = eoc.equation();
                        if (equation is null)
                            continue;
                        // A connect is reported in Connections, not here. One the parser recovered
                        // with a port missing is not one ConnectClauses reports, so it stays here
                        // rather than vanishing from both.
                        if (equation.connect_clause()?.component_reference() is not { Length: >= 2 })
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
                        if (soc.statement() is null && soc.c_comment() is { } algComments)
                        {
                            foreach (var algComment in algComments)
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

        return new ClassBehavior(equations, ConnectClauses.In(composition), statements, hasEquation, hasAlgorithm);
    }

    private static string Slice(string code, int start, int stop)
        => start >= 0 && stop >= start && stop < code.Length ? code[start..(stop + 1)] : string.Empty;
}
