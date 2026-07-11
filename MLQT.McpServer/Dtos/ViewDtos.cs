namespace MLQT.McpServer.Dtos;

// DTOs for the "views" tools — token-efficient projections of a class so an agent can learn how to use
// it (or what it contains) without reading its full source. See ViewTools.

/// <summary>The public interface of a class: how to use it without reading its implementation. Parameters,
/// connectors, components and the function signature include members inherited via extends (each marked
/// with the base class it came from) unless include_inherited is false.</summary>
public sealed record ClassInterfaceView(
    string Id,
    string Name,
    string ClassType,
    bool IsPartial,
    string? Description,
    IReadOnlyList<string> Extends,
    IReadOnlyList<ParameterView> Parameters,
    IReadOnlyList<ConnectorView> Connectors,
    IReadOnlyList<MemberView> PublicComponents,
    FunctionSignatureView? FunctionSignature);

/// <summary>A settable parameter/constant (or a function argument). InheritedFrom is the base class id
/// it comes from, or null if declared in the class itself.</summary>
public sealed record ParameterView(
    string Name,
    string? Type,
    string? Variability,
    string? Default,
    string? Description,
    string? InheritedFrom);

/// <summary>A connector member (physical connector, or a causal signal port).</summary>
public sealed record ConnectorView(
    string Name,
    string? Type,
    string? Causality,
    string? Connection,
    bool TypeIsConnector,
    string? Description,
    string? InheritedFrom);

/// <summary>A public component that is neither a parameter nor a connector (e.g. a record field).</summary>
public sealed record MemberView(
    string Name,
    string? Type,
    string? Description,
    string? InheritedFrom);

/// <summary>A function's inputs and outputs, in declaration order.</summary>
public sealed record FunctionSignatureView(
    IReadOnlyList<ParameterView> Inputs,
    IReadOnlyList<ParameterView> Outputs);

/// <summary>One raw element from list_class_elements. InheritedFrom is the base class id it comes from,
/// or null if declared in the class itself.</summary>
public sealed record ClassElementView(
    string Kind,
    string Name,
    string? Type,
    string? Variability,
    string? Causality,
    string? Connection,
    string Visibility,
    string? Default,
    string? Description,
    string? ClassType,
    IReadOnlyList<string> Prefixes,
    int Line,
    string? InheritedFrom);

/// <summary>Full element listing for a class.</summary>
public sealed record ClassElementsResult(
    string Id,
    int Count,
    IReadOnlyList<ClassElementView> Elements);

/// <summary>Documentation of a class: its description plus the Documentation annotation strings.</summary>
public sealed record ClassDocumentationResult(
    string Id,
    string Format,
    string? Description,
    string? Info,
    string? Revisions);

/// <summary>A reference that these (best-effort) resolution rules could not resolve to a loaded class.</summary>
public sealed record UnresolvedReference(
    string Name,
    string Kind,
    int Line);

/// <summary>Result of validate_class_references.</summary>
public sealed record ReferenceValidationResult(
    string Id,
    int Checked,
    int UnresolvedCount,
    IReadOnlyList<UnresolvedReference> Unresolved,
    string Note);
