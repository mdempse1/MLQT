namespace MLQT.McpServer.Dtos;

/// <summary>
/// One operation of <c>mlqt_edit_class</c>. <see cref="Op"/> says which; each operation reads only the
/// fields it names, and the class is the tool's own argument, not a field here.
/// </summary>
public sealed class ClassEditOperation
{
    public string Op { get; set; } = string.Empty;

    // replace; All replaces every occurrence of Old rather than requiring exactly one
    public string? Old { get; set; }
    public string? New { get; set; }
    public bool? All { get; set; }

    // The element operations, named as the fields of the mlqt_ tools that do the same edit.
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Modifier { get; set; }
    public string? Description { get; set; }
    public string? BaseType { get; set; }
    public string? Import { get; set; }
    public string? Equation { get; set; }
    public string? Statement { get; set; }
    public string? PortA { get; set; }
    public string? PortB { get; set; }
    public string? Comment { get; set; }
    public string? Visibility { get; set; }
    public string? Prefix { get; set; }
    public string? ConstrainedBy { get; set; }
    public string? Condition { get; set; }

    // set_component_placement
    public int? X1 { get; set; }
    public int? Y1 { get; set; }
    public int? X2 { get; set; }
    public int? Y2 { get; set; }
    public int? Rotation { get; set; }

    // set_class_documentation
    public string? Info { get; set; }
    public string? Revisions { get; set; }

    // suppress_rule, accept_spelling, correct_spelling
    public string? RuleId { get; set; }
    public string? Component { get; set; }
    public string? Reason { get; set; }
    public string? Word { get; set; }
    public string? NewWord { get; set; }
}

/// <summary>What <c>mlqt_edit_class</c> did: the file it changed, or with preview the text it would have
/// written, and any notes the operations returned (for example a type that does not resolve).</summary>
public sealed record EditClassResult(
    string ClassId,
    string FilePath,
    bool PreviewOnly,
    int OperationsApplied,
    string? NewFileContent,
    IReadOnlyList<string> Notes);
