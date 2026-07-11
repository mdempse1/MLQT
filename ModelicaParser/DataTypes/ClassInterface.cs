namespace ModelicaParser.DataTypes;

/// <summary>The kind of a structural element extracted from a Modelica class.</summary>
public enum ClassElementKind
{
    /// <summary>A component/variable/parameter/connector declaration.</summary>
    Component,
    /// <summary>An <c>extends</c> (inheritance) clause.</summary>
    Extends,
    /// <summary>An <c>import</c> clause.</summary>
    Import,
    /// <summary>A nested class definition (listed but not recursed into).</summary>
    Class
}

/// <summary>
/// One structural element of a Modelica class, produced by
/// <see cref="Visitors.ClassInterfaceExtractor"/>. Purely syntactic: it reports what is written in the
/// class (name, declared type text, prefixes, description) and performs no cross-class resolution
/// (e.g. it does not know whether a component's type is a connector — that needs the graph).
/// </summary>
public sealed record ClassElement
{
    /// <summary>What kind of element this is.</summary>
    public required ClassElementKind Kind { get; init; }

    /// <summary>Component name; nested class name; extends base type; or the import statement text.</summary>
    public required string Name { get; init; }

    /// <summary>Declared type text (component type, or extends base type). Null for imports.</summary>
    public string? Type { get; init; }

    /// <summary>parameter | constant | discrete, or null for a plain (continuous) variable. Components only.</summary>
    public string? Variability { get; init; }

    /// <summary>input | output, or null (acausal). Components only.</summary>
    public string? Causality { get; init; }

    /// <summary>flow | stream, or null. Components only.</summary>
    public string? Connection { get; init; }

    /// <summary>True if the element is in a public section, false if in a protected section.</summary>
    public bool IsPublic { get; init; } = true;

    /// <summary>Binding value or modifier text (e.g. "5" or "(k=1, T=2)"), if present. Components only.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Description string from the trailing comment. Components and nested classes.</summary>
    public string? Description { get; init; }

    /// <summary>Class kind (model/block/package/...) for a nested-class element.</summary>
    public string? ClassType { get; init; }

    /// <summary>Element prefixes present: replaceable, redeclare, final, inner, outer.</summary>
    public IReadOnlyList<string> Prefixes { get; init; } = Array.Empty<string>();

    /// <summary>1-based source line of the element within the parsed class code.</summary>
    public int Line { get; init; }
}

/// <summary>
/// The structural interface of a single Modelica class: its own description plus the flat list of its
/// outermost-level elements. Nested classes appear as <see cref="ClassElementKind.Class"/> entries but
/// are not recursed into (each nested class has its own <c>ModelNode</c> and can be queried directly).
/// </summary>
public sealed record ClassInterface
{
    /// <summary>The class's own description string (the quoted comment after its name), if any.</summary>
    public string? Description { get; init; }

    /// <summary>The class's elements in source order.</summary>
    public IReadOnlyList<ClassElement> Elements { get; init; } = Array.Empty<ClassElement>();
}
