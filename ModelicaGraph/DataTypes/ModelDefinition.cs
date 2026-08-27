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

    private string _modelicaCode = string.Empty;

    /// <summary>
    /// The Modelica source code for this model.
    ///
    /// <para>Replacing it drops anything measured from the old source — the parse tree's own staleness
    /// is already handled by <see cref="EnsureParsed"/>, and <see cref="Coverage"/> would otherwise
    /// describe code that is no longer here.</para>
    /// </summary>
    public string ModelicaCode
    {
        get => _modelicaCode;
        set
        {
            _modelicaCode = value;
            Coverage = null;
        }
    }

    /// <summary>
    /// What this class contributes to the coverage figures, once something has measured it. Null until
    /// then. Measuring means parsing the class and walking its interface, so the answer is kept: it is
    /// the same for every scope the class appears in, and the dashboard asks for several.
    /// </summary>
    public CoverageFacts? Coverage { get; set; }

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

        // Keep whatever the load path already recorded. Those errors came from parsing the whole
        // file, so they carry real file line numbers and the lexer's diagnosis (e.g. "unterminated
        // string literal"). Re-parsing this one class in isolation re-derives the same problem with
        // class-relative positions and less context — overwriting with that used to replace the
        // useful message with a bare "mismatched input ';'" as soon as anything checked the class.
        if (ParserErrors.Count == 0)
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
