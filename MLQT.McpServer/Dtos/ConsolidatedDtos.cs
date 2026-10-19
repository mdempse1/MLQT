namespace MLQT.McpServer.Dtos;

/// <summary>What <c>mlqt_session</c> reports for 'status': the libraries loaded, each with the dependencies
/// it declares that are not loaded, and the repositories added.</summary>
public sealed record SessionStatus(IReadOnlyList<LibrarySummary> Libraries, IReadOnlyList<RepositorySummary> Repositories);
