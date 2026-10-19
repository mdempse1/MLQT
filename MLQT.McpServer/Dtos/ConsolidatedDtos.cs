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
