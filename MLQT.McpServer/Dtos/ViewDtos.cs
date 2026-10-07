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
    FunctionSignatureView? FunctionSignature,
    bool RecoveredFromDocumentation = false);

/// <summary>A settable parameter/constant (or a function argument). InheritedFrom is the base class id
/// it comes from, or null if declared in the class itself. Default is the value it takes, and ModifiedIn
/// the class whose modification set it (null when it is the declaration's own); TypeModification
/// is any modification written on its type (e.g. "(min=0)"), which is a constraint, not a value.
///
/// <para>Unit is only ever set for a class recovered from documentation, where the vendor's table
/// states it and there is no type to read it from. For a class read from source the unit lives in
/// the declared type, as Modelica writes it.</para></summary>
public sealed record ParameterView(
    string Name,
    string? Type,
    string? Variability,
    string? Default,
    string? TypeModification,
    string? Description,
    string? InheritedFrom,
    string? Unit = null,
    string? ModifiedIn = null);

/// <summary>A connector member (physical connector, or a causal signal port).</summary>
public sealed record ConnectorView(
    string Name,
    string? Type,
    string? Causality,
    string? Connection,
    bool TypeIsConnector,
    string? Description,
    string? InheritedFrom,
    string? Unit = null,
    string? Condition = null);

/// <summary>A public component that is neither a parameter nor a connector (e.g. a record field).</summary>
public sealed record MemberView(
    string Name,
    string? Type,
    string? Description,
    string? InheritedFrom,
    string? Unit = null);

/// <summary>A function's inputs and outputs, in declaration order.</summary>
public sealed record FunctionSignatureView(
    IReadOnlyList<ParameterView> Inputs,
    IReadOnlyList<ParameterView> Outputs);

/// <summary>One raw element from mlqt_list_class_elements. InheritedFrom is the base class id it comes from,
/// or null if declared in the class itself. Condition is the expression a conditional component is
/// declared with, and such a component exists only where that expression is true. ModifiedIn is the
/// class whose modification set Default, or null when it is the declaration's own binding.
/// ArraySubscripts are the element's dimensions as written; Redeclarations are what its modification
/// replaces, keyed by the path to the element replaced.</summary>
public sealed record ClassElementView(
    string Kind,
    string Name,
    string? Type,
    string? Variability,
    string? Causality,
    string? Connection,
    string Visibility,
    string? Default,
    string? TypeModification,
    string? Description,
    string? ClassType,
    IReadOnlyList<string> Prefixes,
    IReadOnlyList<string> LeadingComments,
    int Line,
    string? Condition,
    string? InheritedFrom,
    string? ModifiedIn = null,
    string? ArraySubscripts = null,
    IReadOnlyDictionary<string, RedeclarationView>? Redeclarations = null);

/// <summary>What a redeclaration replaces an element with: ClassType is null when a component is
/// redeclared, and Type is null for a redeclared enumeration.</summary>
public sealed record RedeclarationView(
    string? ClassType,
    string? Type,
    string? TypeModification,
    string? ArraySubscripts);

/// <summary>Full element listing for a class.</summary>
public sealed record ClassElementsResult(
    string Id,
    int Count,
    IReadOnlyList<ClassElementView> Elements,
    bool RecoveredFromDocumentation = false);

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

/// <summary>A class matched by mlqt_search_text, with where it matched and a short snippet.</summary>
public sealed record TextSearchItem(string Id, string Name, string ClassType, string MatchedIn, string Snippet);

public sealed record TextSearchResult(int Total, int Count, IReadOnlyList<TextSearchItem> Items);

/// <summary>A class matched by mlqt_search_by_interface, with its parameter/connector counts.</summary>
public sealed record InterfaceSearchItem(
    string Id, string Name, string ClassType, int ParameterCount, int ConnectorCount, bool HasExperiment);

public sealed record InterfaceSearchResult(int Total, int Count, IReadOnlyList<InterfaceSearchItem> Items);

/// <summary>A component's diagram placement: its bounding extent [x1,y1,x2,y2] and optional rotation.</summary>
/// <summary>
/// A component on a class's diagram. Extent is [x1,y1,x2,y2] in the diagram's own coordinates, with
/// any <c>origin</c> from the Placement already added in; InheritedFrom names the base class that
/// declared it, or is null when the class declares it itself.
/// </summary>
public sealed record DiagramComponent(
    string Name, string? Type, IReadOnlyList<int>? Extent, int? Rotation, string? InheritedFrom = null);

/// <summary>The diagram layout of a class: its components' placements plus its connections.</summary>
public sealed record DiagramLayoutResult(
    string ClassId,
    IReadOnlyList<DiagramComponent> Components,
    IReadOnlyList<ConnectionView> Connections);

/// <summary>A connect(a, b) equation. <paramref name="Within"/> is null for one written directly in an
/// equation section, and otherwise the for/if/when branches it is inside, outermost first
/// (<c>["for i in 1:n"]</c>) - the ports of a connect in a loop are subscripted by its indices.</summary>
public sealed record ConnectionView(string PortA, string PortB, IReadOnlyList<string>? Within = null)
{
    public static ConnectionView Of(ModelicaParser.DataTypes.ConnectClause clause)
        => new(clause.PortA, clause.PortB, clause.IsNested ? clause.Within : null);
}

/// <summary>The connections in a class: its own, plus base classes that themselves contain connections
/// (behavior is not merged — query those bases directly to see their connections).</summary>
public sealed record ConnectionsResult(
    string ClassId,
    IReadOnlyList<ConnectionView> Connections,
    IReadOnlyList<string> BasesWithConnections);

/// <summary>The behavior a class declares itself, plus base classes that declare behavior (not merged —
/// query those base classes to see theirs). Members are merged in the interface views, but behavior is
/// left in its declaring class.</summary>
/// <summary>An equation or statement plus any // or /* */ comments written immediately before it.</summary>
public sealed record BehaviorLineView(string Text, IReadOnlyList<string> LeadingComments);

public sealed record ClassBehaviorResult(
    string ClassId,
    IReadOnlyList<BehaviorLineView> Equations,
    IReadOnlyList<ConnectionView> Connections,
    IReadOnlyList<BehaviorLineView> Statements,
    IReadOnlyList<string> BasesWithBehavior);

/// <summary>Result of mlqt_validate_class_references.</summary>
public sealed record ReferenceValidationResult(
    string Id,
    int Checked,
    int UnresolvedCount,
    IReadOnlyList<UnresolvedReference> Unresolved,
    string Note);
