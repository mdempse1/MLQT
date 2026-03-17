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
    /// Starting line number in the source file.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number in the source file.
    /// </summary>
    public int StopLine { get; set; }

    /// <summary>
    /// Whether this is a nested model (contained within another model).
    /// </summary>
    public bool IsNested { get; set; }

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
