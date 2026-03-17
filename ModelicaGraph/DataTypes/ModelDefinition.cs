using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph.DataTypes;

/// <summary>
/// Represents a Modelica model definition.
/// </summary>
public class ModelDefinition
{
    /// <summary>
    /// Name of the Modelica model.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The Modelica source code for this model.
    /// </summary>
    public string ModelicaCode { get; set; }

    /// <summary>
    /// Antlr4 code context for the class definition.
    /// Lazily parsed on first access via <see cref="EnsureParsed"/>.
    /// </summary>
    public modelicaParser.Stored_definitionContext? ParsedCode { get; set; }

    /// <summary>
    /// Ensures that ParsedCode is populated, parsing ModelicaCode if needed.
    /// Returns the parse tree (never null unless ModelicaCode is empty).
    /// </summary>
    public modelicaParser.Stored_definitionContext? EnsureParsed()
    {
        if (ParsedCode != null)
            return ParsedCode;

        if (string.IsNullOrWhiteSpace(ModelicaCode))
            return null;

        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(ModelicaCode);
        ParsedCode = parseTree;
        ParserErrors = errors;
        return ParsedCode;
    }

    /// <summary>
    /// Flag to indicate whether the style rules have been checked or not
    /// </summary>
    public Boolean StyleRulesChecked { get; set; } = false;

    /// <summary>
    /// Style rule violations
    /// </summary>
    public List<LogMessage> StyleRuleViolations { get; set; } = new();

    /// <summary>
    /// Parser errors encountered when parsing this model
    /// </summary>
    public List<ParserError> ParserErrors { get; set; } = new();

    public ModelDefinition(string name, string modelicaCode = "")
    {
        Name = name;
        ModelicaCode = modelicaCode;
    }

    public override string ToString()
    {
        return $"Model: {Name} ({ModelicaCode.Length} chars)";
    }
}
