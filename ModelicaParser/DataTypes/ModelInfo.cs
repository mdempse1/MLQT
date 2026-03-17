namespace ModelicaParser.DataTypes;

/// <summary>
/// Represents information about a Modelica model found in a file.
/// </summary>
public class ModelInfo
{
    /// <summary>
    /// Name of the model (identifier).
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Full Modelica source code for this model.
    /// </summary>
    public string SourceCode { get; set; }

    /// <summary>
    /// Type of class (model, block, function, etc.).
    /// </summary>
    public string ClassType { get; set; }

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
    /// Name of the parent model if this is nested, null otherwise.
    /// </summary>
    public string? ParentModelName { get; set; }

    /// <summary>
    /// Whether this class can be stored as a standalone file.
    /// False if the class has prefixes like replaceable, redeclare, inner, outer that prevent standalone storage.
    /// </summary>
    public bool CanBeStoredStandalone { get; set; } = true;

    /// <summary>
    /// Element-level prefix keywords (e.g., "redeclare", "inner replaceable") that precede
    /// the class definition in the parent element rule. Empty when the class has no element prefix.
    /// These prefixes are not part of the class_definition grammar rule and cannot appear
    /// in a stored_definition, so they are stored separately for display purposes.
    /// </summary>
    public string ElementPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Version of this package
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Dictionary of packages and version number used by this package
    /// </summary>
    public Dictionary<string, string>? Uses { get; set; }


    public ModelInfo(string name, string sourceCode, string classType)
    {
        Name = name;
        SourceCode = sourceCode;
        ClassType = classType;
    }

    public override string ToString() => $"{ClassType} {Name} (Lines {StartLine}-{StopLine})";
}
