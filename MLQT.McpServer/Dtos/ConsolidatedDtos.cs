namespace MLQT.McpServer.Dtos;

/// <summary>What <c>mlqt_session</c> reports for 'status': the libraries loaded, each with the dependencies
/// it declares that are not loaded, and the repositories added.</summary>
public sealed record SessionStatus(IReadOnlyList<LibrarySummary> Libraries, IReadOnlyList<RepositorySummary> Repositories);

/// <summary>One class as <c>mlqt_get_class</c> read it: each view asked for, holding what that view's own
/// tool answers (an error included), or <see cref="Error"/> alone when there is no such class.</summary>
public sealed record ClassViews(
    string ClassId,
    string? Error = null,
    object? Info = null,
    object? Source = null,
    object? Interface = null,
    object? Elements = null,
    object? Behavior = null,
    object? Connections = null,
    object? Documentation = null,
    object? Layout = null,
    object? Resources = null);

/// <summary>What <c>mlqt_get_class</c> read: each class asked for, in order.</summary>
public sealed record GetClassResult(IReadOnlyList<ClassViews> Classes);

/// <summary>What <c>mlqt_check</c> ran: each check asked for, holding what its own tool answers (an error
/// included), and for spelling the suggestions for each word it flagged.</summary>
public sealed record CheckFacadeResult(
    object? Style,
    object? Spelling,
    IReadOnlyList<SpellSuggestionsResult>? SpellingSuggestions,
    object? References,
    object? Resources);

/// <summary>One class's answer from <c>mlqt_dependencies</c>: what it uses, or what uses it.</summary>
public sealed record ClassDependencies(string ClassId, object Answer);

/// <summary>What <c>mlqt_dependencies</c> found for each class asked about, in order.</summary>
public sealed record DependenciesFacadeResult(string Direction, IReadOnlyList<ClassDependencies> Classes);
