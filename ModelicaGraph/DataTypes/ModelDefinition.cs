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
    /// <para>Replacing it drops anything read from the old source — the parse tree's own staleness
    /// is already handled by <see cref="EnsureParsed"/>, while <see cref="Coverage"/> and
    /// <see cref="Suppressions"/> would otherwise describe code that is no longer here.</para>
    /// </summary>
    public string ModelicaCode
    {
        get => _modelicaCode;
        set
        {
            _modelicaCode = value;
            Coverage = null;
            Suppressions = null;
        }
    }

    /// <summary>
    /// What this class contributes to the coverage figures, once something has measured it. Null until
    /// then. Measuring means parsing the class and walking its interface, so the answer is kept: it is
    /// the same for every scope the class appears in, and the dashboard asks for several.
    /// </summary>
    public CoverageFacts? Coverage { get; set; }

    /// <summary>
    /// The <c>__MLQT</c> directives this class carries, once something has read them. Null until then;
    /// <see cref="ModelicaParser.StyleRules.SuppressionSet.Empty"/> — one shared instance — when the
    /// class carries none, which is nearly every class.
    ///
    /// <para>Kept because three passes want it and each was walking the tree for itself: the style
    /// checker filters its findings through it, the coverage measurer asks whether the class opted out
    /// of formatting, and the graph analyses filter their own findings the same way, re-parsing to do
    /// so. Set it through <see cref="ClassSuppressions.For"/> rather than directly — that is where the
    /// walk and the shared-empty convention live.</para>
    /// </summary>
    public ModelicaParser.StyleRules.SuppressionSet? Suppressions { get; set; }

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
        //
        // And record nothing at all when the load already dealt with this file's errors. The whole
        // file was parsed once and each error attributed to the innermost class whose text it is in;
        // every class enclosing that one fails to parse for the same reason, so letting each record
        // its own copy gives one problem as many owners as it has ancestors.
        if (MayRecordParserErrors && ParserErrors.Count == 0)
            ParserErrors = errors;

        return ParsedCode;
    }

    /// <summary>
    /// Parses the class if it is not parsed already, runs <paramref name="use"/> against the tree,
    /// and releases the tree again <b>only if this call is what parsed it</b>. Returns
    /// <paramref name="ifUnparseable"/> for a class with no source or source that will not parse.
    ///
    /// <para>This is <b>the</b> way to read a class you do not own, and there should be no other. A
    /// parse tree is far larger than the source it came from, and a run over a real library touches
    /// tens of thousands of classes, so anything that parses one for a moment has to give it back —
    /// but only if it was the one that took it, because releasing a tree the caller upstream was
    /// still using costs that caller the re-parse it was avoiding. Written out by hand at each site,
    /// the two halves came apart in both directions: some places kept a tree they had taken for a
    /// moment, and others released one they had merely been handed. Neither half is visible in a
    /// diff of the code that reads the tree, which is why it is a primitive rather than a rule.</para>
    ///
    /// <para>The resolvers keep no trees. What their caches hold is the <em>answer</em> — a resolved
    /// base class, whether a type chain fixes a unit — so a type already resolved is never re-parsed
    /// and handing the tree back costs nothing. That was worth writing down because the opposite was
    /// assumed for a while, and it is the reasoning to repeat before any new "deliberate" exception:
    /// cache what was worked out, not what it was worked out from.</para>
    ///
    /// <para><b>Not</b> for the bulk load pass. <c>GraphBuilder</c> parses every class in the library
    /// in turn and clears <see cref="ParsedCode"/> unconditionally afterwards, which is right there
    /// and would be wrong here: it <em>is</em> the outermost reader, nothing upstream is holding
    /// anything, and bounding the memory of a pass over tens of thousands of classes is the point.
    /// Borrowing would keep a tree it had merely found lying around. The distinction is ownership,
    /// not the shape of the code.</para>
    /// </summary>
    /// <inheritdoc cref="Borrow{T}"/>
    public void Borrow(Action<modelicaParser.Stored_definitionContext> use)
        => Borrow<bool>(tree => { use(tree); return true; });

    /// <inheritdoc cref="Borrow{T}"/>
    public T Borrow<T>(Func<modelicaParser.Stored_definitionContext, T> use, T ifUnparseable = default!)
    {
        var owned = ParsedCode is null;
        var tree = EnsureParsed();
        if (tree is null)
            return ifUnparseable;

        try
        {
            return use(tree);
        }
        finally
        {
            if (owned)
                ParsedCode = null;
        }
    }

    /// <summary>
    /// Flag to indicate whether the style rules have been checked or not
    /// </summary>
    public Boolean StyleRulesChecked { get; set; } = false;

    /// <summary>
    /// Style rule findings
    /// </summary>
    public List<LogMessage> StyleRuleFindings { get; set; } = new();

    /// <summary>
    /// Parser errors encountered when parsing this model
    /// </summary>
    public List<ParserError> ParserErrors { get; set; } = new();

    /// <summary>
    /// Whether parsing this class's stored code may record <see cref="ParserErrors"/> against it.
    ///
    /// <para>Cleared for every class in a file that failed to parse, because the load has already
    /// attributed each of that file's errors to the innermost class whose text it is in. Every class
    /// enclosing that one contains the same broken text and fails for the same reason, so letting
    /// each record its own copy on first parse gave one problem as many owners as it had ancestors —
    /// and every surface that walks the graph then reported it that many times.</para>
    ///
    /// <para>Left set for a class from a file that parsed cleanly: if its own stored source somehow
    /// does not parse, that is news, and it is how a class held only in memory reports at all.</para>
    /// </summary>
    public bool MayRecordParserErrors { get; set; } = true;

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
