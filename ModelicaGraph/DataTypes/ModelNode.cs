using ModelicaParser.DataTypes;
using RevisionControl;

namespace ModelicaGraph.DataTypes;

/// <summary>
/// Represents a Modelica model node in the graph.
/// A model can use (depend on) multiple other models.
/// </summary>
public class ModelNode : GraphNode
{
    /// <summary>
    /// The Modelica model definition.
    /// </summary>
    public ModelDefinition Definition { get; set; }

    /// <summary>
    /// ID of the file that contains this model.
    /// </summary>
    public string? ContainingFileId { get; set; }

    /// <summary>
    /// IDs of models that this model uses/depends on.
    /// </summary>
    public HashSet<string> UsedModelIds { get; }

    /// <summary>
    /// IDs of models that use/depend on this model.
    /// </summary>
    public HashSet<string> UsedByModelIds { get; }

    /// <summary>
    /// IDs of resource nodes (files and directories) that this model references.
    /// </summary>
    public HashSet<string> ReferencedResourceIds { get; }

    // --- Parser-derived metadata ---

    /// <summary>
    /// Type of class (model, block, function, connector, record, type, package, class).
    /// </summary>
    public string ClassType { get; set; } = "model";

    /// <summary>
    /// Whether the class carries the <c>partial</c> prefix — intended to be
    /// extended, not instantiated directly. Captured separately from
    /// <see cref="ClassType"/>, which only records the restriction keyword.
    /// </summary>
    public bool IsPartial { get; set; }

    /// <summary>
    /// Starting line number in the source file.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// True while the stored <c>Definition.ModelicaCode</c> is still the verbatim slice of the file
    /// that starts at <see cref="StartLine"/> — which is what lets a line inside the class be mapped
    /// back to a line in the file by adding the offset.
    ///
    /// <para>False once something has rewritten the stored source: trimming a package's inline
    /// children, or re-rendering a class through the formatter. The text is then still correct
    /// Modelica, but its lines are the renderer's, not the file's, and a report that added the offset
    /// anyway would point at a real line that says something else. Consumers fall back to the class
    /// declaration, which is never wrong about which class is meant.</para>
    /// </summary>
    public bool SourceMatchesFile { get; set; } = true;

    /// <summary>
    /// Ending line number in the source file.
    /// </summary>
    public int StopLine { get; set; }

    /// <summary>
    /// Zero-based character offset of the first character of the
    /// <c>class_definition</c> rule in the underlying source file, read with
    /// <see cref="System.Text.Encoding.Latin1"/> to match the parser.
    /// <c>-1</c> when not populated (legacy placeholder nodes, snapshots
    /// written before the field existed, etc.). Used by snapshot rehydration
    /// to slice the same character range the parser originally captured —
    /// line-based slicing alone can leak preceding element prefixes
    /// (<c>replaceable</c>, <c>redeclare</c>, …) into the rehydrated
    /// <see cref="ModelDefinition.ModelicaCode"/> and break re-parsing.
    /// </summary>
    public int StartIndex { get; set; } = -1;

    /// <summary>
    /// Zero-based character offset of the last character of the
    /// <c>class_definition</c> rule (inclusive) in the underlying source file.
    /// <c>-1</c> when not populated. See <see cref="StartIndex"/>.
    /// </summary>
    public int StopIndex { get; set; } = -1;

    /// <summary>
    /// Whether this is a nested model (contained within another model).
    /// </summary>
    public bool IsNested { get; set; }

    /// <summary>
    /// Whether the class sits in a public section of its enclosing class — false only for one
    /// declared after a <c>protected</c> keyword. Top-level classes are always public.
    ///
    /// Captured at load time from the parse tree. Consumers must use this rather than re-deriving
    /// visibility from the parent package's stored source: that source has its standalone children
    /// trimmed out as a memory optimisation, so the answer would otherwise depend on whether the
    /// trim had run — which differs between a fresh load and a file reload.
    /// </summary>
    public bool IsPublic { get; set; } = true;

    /// <summary>
    /// Set once <see cref="PackageCodeTrimmer"/> has processed this package, so a repeated trim is a
    /// no-op instead of re-parsing and re-rendering it. A reload replaces the node, which clears the
    /// flag and lets the reloaded source be trimmed again.
    /// </summary>
    public bool ChildrenTrimmed { get; set; }

    /// <summary>
    /// Name of the parent model/package.
    /// </summary>
    public string? ParentModelName { get; set; }

    /// <summary>
    /// Whether this class can be stored as a standalone file.
    /// False if the class has prefixes like replaceable, redeclare, inner, outer.
    /// </summary>
    public bool CanBeStoredStandalone { get; set; } = true;

    /// <summary>
    /// Whether this model has a standard Modelica <c>experiment(...)</c> annotation.
    /// </summary>
    public bool HasExperimentAnnotation { get; set; }

    /// <summary>
    /// Element-level prefix keywords (e.g., "redeclare", "inner replaceable") that precede
    /// the class definition. Empty when the class has no element prefix.
    /// </summary>
    public string ElementPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Version of this package from annotation.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Dictionary of packages and version numbers used by this package.
    /// </summary>
    public Dictionary<string, string>? Uses { get; set; }

    // --- Graph-derived ordering ---

    /// <summary>
    /// Child ordering from package.order file.
    /// </summary>
    public string[]? PackageOrder { get; set; }

    /// <summary>
    /// Ordering of nested children within this model.
    /// </summary>
    public string[]? NestedChildrenOrder { get; set; }

    // --- External resource parameters ---

    /// <summary>
    /// Parameter names with loadSelector annotations.
    /// </summary>
    public List<string> LoadSelectorParameters { get; set; } = new();

    /// <summary>
    /// Parameter names with loadResource calls.
    /// </summary>
    public List<string> LoadResourceParameters { get; set; } = new();

    // --- UI display properties ---

    /// <summary>
    /// The ID of the library this model belongs to.
    /// </summary>
    public string LibraryId { get; set; } = "";

    /// <summary>
    /// SVG markup for the Modelica icon annotation, if available.
    /// </summary>
    public string? IconSvg { get; set; }

    /// <summary>
    /// Gets whether this node has a custom Modelica icon.
    /// </summary>
    public bool HasCustomIcon => !string.IsNullOrEmpty(IconSvg);

    /// <summary>
    /// VCS file status of the file containing this model, if applicable.
    /// </summary>
    public VcsFileStatus? FileStatus { get; set; }

    /// <summary>
    /// True when this node is a placeholder that stands in for a file whose contents
    /// could not be parsed. The full source is preserved in <see cref="Definition"/>.ModelicaCode
    /// and the failure is recorded in <see cref="Definition"/>.ParserErrors as a
    /// <see cref="ParserError"/> with severity <see cref="ParserErrorSeverity.FatalParseFailure"/>.
    /// Downstream tooling should skip these nodes for dependency analysis, style checking,
    /// and formatting, but may still present them to the user (e.g., in the library tree).
    /// </summary>
    public bool IsParseFailurePlaceholder { get; set; }

    /// <summary>
    /// True when this node does not stand for readable source at all, but for a class recovered
    /// from a vendor's generated documentation because the library ships encrypted.
    /// <see cref="ModelDefinition.ModelicaCode"/> holds a synthesized declaration carrying only
    /// what the documentation stated — name, description, base classes, whether there is an icon —
    /// so that reference resolution, extends-chain walking and icon inheritance work across the
    /// boundary without every rule needing to know the class came from a different source.
    ///
    /// <para>Because that code is a reconstruction and not the vendor's source, a stub must never
    /// reach a path that <b>writes</b>: formatting, saving, package restructuring, VCS staging or
    /// commit. Those paths reject stubs outright rather than skipping them quietly, so a missing
    /// guard surfaces as a failing test instead of as a rewritten third-party library on a user's
    /// machine. Stubs are likewise never <b>reported</b> on — they are loaded to resolve
    /// references, and findings about a vendor's library are not the user's to fix.</para>
    /// </summary>
    public bool IsExternalStub { get; set; }

    /// <summary>
    /// True when any <see cref="ParserError"/> has been recorded against this model —
    /// either a recoverable syntax error or a fatal parse failure. Convenience for UI
    /// code that needs to flag problem models in the tree and code viewer.
    /// </summary>
    public bool HasParserErrors => Definition?.ParserErrors.Count > 0;

    /// <summary>
    /// True when a <see cref="ParserErrorSeverity.FatalParseFailure"/> error is attached
    /// — the file's contents could not be parsed at all. Implies either a placeholder or
    /// a model whose file crashed the extractor.
    /// </summary>
    public bool HasFatalParseFailure =>
        Definition?.ParserErrors.Any(e => e.Severity == ParserErrorSeverity.FatalParseFailure) == true;

    /// <summary>
    /// Indicates whether any descendant model has uncommitted VCS changes.
    /// </summary>
    public bool HasDescendantChanges { get; set; }

    public ModelNode(string id, string modelName, string modelicaCode = "")
        : base(id, NodeType.Model, modelName)
    {
        Definition = new ModelDefinition(modelName, modelicaCode);
        UsedModelIds = new HashSet<string>();
        UsedByModelIds = new HashSet<string>();
        ReferencedResourceIds = new HashSet<string>();
    }

    public ModelNode(string id, ModelDefinition definition)
        : base(id, NodeType.Model, definition.Name)
    {
        Definition = definition;
        UsedModelIds = new HashSet<string>();
        UsedByModelIds = new HashSet<string>();
        ReferencedResourceIds = new HashSet<string>();
    }

    /// <summary>
    /// Adds a dependency to another model.
    /// </summary>
    public void AddUsedModel(string modelId) => UsedModelIds.Add(modelId);

    /// <summary>
    /// Removes a dependency to another model.
    /// </summary>
    public void RemoveUsedModel(string modelId) => UsedModelIds.Remove(modelId);

    /// <summary>
    /// Adds a reverse dependency (another model uses this one).
    /// </summary>
    public void AddUsedByModel(string modelId) => UsedByModelIds.Add(modelId);

    /// <summary>
    /// Removes a reverse dependency.
    /// </summary>
    public void RemoveUsedByModel(string modelId) => UsedByModelIds.Remove(modelId);

    /// <summary>
    /// Adds a reference to a resource (file or directory).
    /// </summary>
    public void AddReferencedResource(string resourceId) => ReferencedResourceIds.Add(resourceId);

    public override string ToString()
    {
        return $"Model: {Definition.Name} (Uses: {UsedModelIds.Count}, UsedBy: {UsedByModelIds.Count}, Resources: {ReferencedResourceIds.Count})";
    }
}
