using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph.DataTypes;

/// <summary>
/// Represents a Modelica model definition.
/// </summary>
/// <remarks>
/// <para><b>Threads.</b> While nothing replaces <see cref="ModelicaCode"/>, several threads may use
/// the same instance at once to:</para>
/// <list type="bullet">
/// <item>read <see cref="ModelicaCode"/></item>
/// <item>call <see cref="EnsureParsed"/> and <see cref="Borrow{T}"/>, and release the tree by
/// setting <see cref="ParsedCode"/> to null</item>
/// <item>render the icon and publish it in <see cref="IconSvg"/>, <see cref="IconGeneration"/>
/// and, last, <see cref="IconRendered"/></item>
/// </list>
/// <para>Replacing <see cref="ModelicaCode"/> is the one thing that has to run with no other
/// thread using the instance. A host gets that by not editing or reloading while it reads.</para>
/// <para>The cached readers (<c>ClassSuppressions</c>, <c>ClassImports</c>) take the instance's own
/// lock. The other derived state, such as <see cref="Coverage"/> and
/// <see cref="StyleRuleFindings"/>, belongs to the pass that computes it, and is not covered by
/// this promise.</para>
/// </remarks>
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
    /// <para>Replacing it drops everything read from the old source: <see cref="ParsedCode"/>,
    /// <see cref="Coverage"/>, <see cref="Suppressions"/>, <see cref="Imports"/>,
    /// <see cref="IsEncapsulated"/>, <see cref="Bases"/> and
    /// <see cref="ParserErrors"/> all describe code that is no longer here, and
    /// <see cref="MayRecordParserErrors"/> is set again so the new code can report its own. The icon is marked to be rendered again rather than dropped -
    /// see <see cref="IconSvg"/>.</para>
    ///
    /// <para>The tree is on that list, and the comment here used to say it was not — that
    /// <see cref="EnsureParsed"/> handled its own staleness. It does not: it returns
    /// <see cref="ParsedCode"/> whenever it is non-null and never looks at the source. So a caller
    /// replacing the source while a tree was live got the old tree back, and the suppressions cleared
    /// on the line above were then rebuilt from it — undoing the clearing in the same setter that did
    /// it. Every current caller nulls the tree itself or never had one, which is exactly why the
    /// comment would have gone on being believed.</para>
    /// </summary>
    public string ModelicaCode
    {
        get => _modelicaCode;
        set
        {
            // Under the lock the cached readers take (ClassImports, ClassSuppressions): a read already
            // under way finishes from the old source and is then cleared, rather than publishing its
            // answer after the clearing - which left the old source's imports and bases on the class.
            lock (this)
                ReplaceSource(value);
        }
    }

    private void ReplaceSource(string value)
    {
        _modelicaCode = value;
        ParsedCode = null;
        Coverage = null;
        Suppressions = null;
        Imports = null;
        IsEncapsulated = null;
        Bases = null;
        // A diagnosis of the old source says nothing about the new one, and the rule barring a
        // class from recording its own was the old file's (B389). Kept, they reported errors a
        // reformatted class no longer has and hid the ones it does. PackageCodeTrimmer, which
        // only cuts lines out, puts the load's diagnosis back itself.
        ParserErrors = new();
        MayRecordParserErrors = true;
        // Rendered again when the tree next asks, but not blanked meanwhile (B300).
        IconRendered = false;
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
    /// The import statements the class declares, once something has read them. Null until then.
    /// Kept because every class inside a package asks for the package's imports when it resolves a
    /// name (B292). Set it through <see cref="ClassImports.For"/> rather than directly.
    /// </summary>
    public IReadOnlyList<string>? Imports { get; set; }

    /// <summary>
    /// Whether the class is <c>encapsulated</c> - name lookup does not continue past it into the
    /// classes enclosing it (MLS §5.3.1) - once something has read it. Null until then. Read with
    /// <see cref="Imports"/>, in the same pass, because lookup asks every enclosing class both
    /// questions. Set it through <see cref="ClassImports.IsEncapsulated"/> rather than directly.
    /// </summary>
    public bool? IsEncapsulated { get; set; }

    /// <summary>
    /// The bases the class names, as written - each <c>extends</c> clause's, or a short class's one -
    /// once something has read them. Null until then. Read with <see cref="Imports"/>, in the same
    /// pass. Set it through <see cref="ClassImports.BasesOf"/> rather than directly.
    /// </summary>
    public IReadOnlyList<string>? Bases { get; set; }

    /// <summary>
    /// The class's icon as SVG, once something has rendered it, and whether that has been tried.
    ///
    /// <para><b>Here rather than on the node because it is derived from the code</b>, like
    /// <see cref="Coverage"/> and <see cref="Suppressions"/> — which means it goes stale exactly
    /// when they do, and this is the one place that knows. It lived on <c>ModelNode</c>, outside
    /// that invalidation, so nothing could safely keep it and the library browser re-rendered every
    /// top-level icon on every tree refresh: resolving each class's base classes, parsing them to do
    /// it, on the dispatcher. Measured on a real project, one refresh of one repository's tree cost
    /// **1,477ms of its 1,522ms** in exactly that (B258).</para>
    ///
    /// <para><see cref="IconRendered"/> is separate from the SVG being null because <em>most classes
    /// have no icon</em>, and "asked, and there is none" has to be as cheap to remember as an
    /// answer.</para>
    ///
    /// <para><b>A code change clears <see cref="IconRendered"/> and leaves the SVG alone</b> (B300).
    /// The library browser draws the SVG straight off the class it already holds, and nothing
    /// renders the icon again until the tree is refreshed - so when this setter blanked the SVG,
    /// <b>Format All Files</b>, which sets every class's code without refreshing the tree, turned every
    /// package on screen into a plain folder until something else rebuilt it. The old icon is
    /// shown until then instead, which for a reformatted class is the right icon anyway: formatting
    /// moves text, not graphics.</para>
    /// </summary>
    public string? IconSvg { get; set; }

    private bool _iconRendered;

    /// <inheritdoc cref="IconSvg"/>
    /// <remarks>
    /// <b>Set last, and read first.</b> Reads and writes of this flag are volatile, so a renderer
    /// that sets <see cref="IconSvg"/> and <see cref="IconGeneration"/> and then this flag publishes
    /// all three together. A thread that reads the flag as set, and then reads the other two, sees
    /// what that renderer wrote. x64 keeps stores in order anyway; ARM64 does not.
    /// </remarks>
    public bool IconRendered
    {
        get => Volatile.Read(ref _iconRendered);
        set => Volatile.Write(ref _iconRendered, value);
    }

    /// <summary>
    /// Which generation of the loaded libraries <see cref="IconSvg"/> was rendered against (B349).
    /// </summary>
    /// <remarks>
    /// An icon is drawn from the class's base classes as well as its own code, often in another
    /// library, so the answer also goes stale when a library arrives or leaves or any class is
    /// reloaded - which this class's code setter cannot see. The library data service counts those
    /// and treats an icon stamped with an older count as not rendered.
    /// </remarks>
    public int IconGeneration { get; set; }

    private modelicaParser.Stored_definitionContext? _parsedCode;

    /// <summary>
    /// Antlr4 code context for the class definition.
    /// Lazily parsed on first access via <see cref="EnsureParsed"/>.
    /// </summary>
    /// <remarks>
    /// Setting it to null <em>releases</em> the tree: it drops the cached reference and nothing else.
    /// A reader already holding the tree keeps a valid tree, because nothing changes a tree once it
    /// is built.
    /// </remarks>
    public modelicaParser.Stored_definitionContext? ParsedCode
    {
        get => Volatile.Read(ref _parsedCode);
        set => Volatile.Write(ref _parsedCode, value);
    }

    /// <summary>
    /// How many times this class has been parsed. For tests: a cache that several threads fill at
    /// once is right when this is one, and every other way of telling needs a stopwatch.
    /// </summary>
    internal int TimesParsed => Volatile.Read(ref _timesParsed);
    private int _timesParsed;

    /// <summary>
    /// Ensures that ParsedCode is populated, parsing ModelicaCode if needed.
    /// Returns the parse tree (never null unless ModelicaCode is empty).
    /// </summary>
    /// <remarks>
    /// <para>Reads <see cref="ParsedCode"/> once, and returns the tree it parsed rather than reading the
    /// property back. Another reader can release the tree at any moment, and reading it twice
    /// returned null for a class with source whenever that happened in between — which dependency
    /// analysis took as nothing to analyse, leaving the class with no edges and no message (B291).</para>
    ///
    /// <para><b>Safe from several threads at once</b>, while nothing replaces
    /// <see cref="ModelicaCode"/>. Two threads that find no tree both parse, but only the first to
    /// finish publishes its tree and records its errors. The other returns the published tree, so
    /// every caller gets the same tree and the errors are recorded once.</para>
    /// </remarks>
    public modelicaParser.Stored_definitionContext? EnsureParsed() => EnsureParsed(out _);

    /// <param name="parsedHere">True when this call is what put the tree in
    /// <see cref="ParsedCode"/>, so the call owns it and may release it. That is decided by the
    /// publish itself, not by an earlier look at <see cref="ParsedCode"/>: looking first and parsing
    /// afterwards let a tree released in between be parsed again by a caller that thought it owned
    /// nothing, and the tree then stayed cached.</param>
    private modelicaParser.Stored_definitionContext? EnsureParsed(out bool parsedHere)
    {
        parsedHere = false;
        if (ParsedCode is { } existing)
            return existing;

        var code = ModelicaCode;
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(code);
        Interlocked.Increment(ref _timesParsed);

        // Only into an empty slot: a tree another thread published meanwhile is the one everybody
        // else has, and this one's errors are a copy of the ones it recorded.
        if (Interlocked.CompareExchange(ref _parsedCode, parseTree, null) is { } published)
            return published;
        parsedHere = true;

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
        //
        // What is recorded here was parsed from this class's own source, so its lines count from the
        // class, not the file - and are marked so, because a report cannot tell by looking (B388).
        if (MayRecordParserErrors && ParserErrors.Count == 0)
        {
            foreach (var error in errors)
                error.LineIsClassRelative = true;
            ParserErrors = errors;
        }

        return parseTree;
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
    /// <para><b>Safe from several threads on the same class</b>, while nothing replaces
    /// <see cref="ModelicaCode"/>. A borrower never gets a null tree for a class whose source parses.
    /// A tree released while a borrower is still using it stays valid. A borrower releases only the
    /// tree its own call published, and only while that tree is still the cached one.</para>
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
        var tree = EnsureParsed(out var owned);
        if (tree is null)
            return ifUnparseable;

        try
        {
            return use(tree);
        }
        finally
        {
            // Releases only the tree this call published. If something released it and another
            // reader cached a tree of its own meanwhile, that tree belongs to that reader.
            if (owned)
                Interlocked.CompareExchange(ref _parsedCode, null, tree);
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
    ///
    /// <para>Set again, with <see cref="ParserErrors"/> emptied, whenever <see cref="ModelicaCode"/>
    /// is replaced: the bar was about the file the class was loaded from, and the new code is not that
    /// file (B389).</para>
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
