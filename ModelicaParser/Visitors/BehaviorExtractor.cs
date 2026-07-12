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
        var composition = ModelicaParserHelper.Parse(classCode)
            ?.class_definition()?.FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();
        if (composition?.children is null)
            return ClassBehavior.Empty;

        var equations = new List<string>();
        var connections = new List<ConnectionPair>();
        var statements = new List<string>();
        var hasEquation = false;
        var hasAlgorithm = false;

        foreach (var child in composition.children)
        {
            switch (child)
            {
                case modelicaParser.Equation_sectionContext eq:
                    hasEquation = true;
                    foreach (var eoc in eq.equation_or_comment())
                    {
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
                            equations.Add(Slice(classCode, equation.Start.StartIndex, equation.Stop.StopIndex));
                        }
                    }
                    break;

                case modelicaParser.Algorithm_sectionContext alg:
                    hasAlgorithm = true;
                    foreach (var soc in alg.statement_or_comment())
                    {
                        var statement = soc.statement();
                        if (statement is not null)
                            statements.Add(Slice(classCode, statement.Start.StartIndex, statement.Stop.StopIndex));
                    }
                    break;
            }
        }

        return new ClassBehavior(equations, connections, statements, hasEquation, hasAlgorithm);
    }

    private static string Slice(string code, int start, int stop)
        => start >= 0 && stop >= start && stop < code.Length ? code[start..(stop + 1)] : string.Empty;
}
