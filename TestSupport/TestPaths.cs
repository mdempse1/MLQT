using System.IO;

namespace MLQT.TestSupport;

/// <summary>
/// Absolute paths for tests, rooted the way the running platform roots them.
/// </summary>
/// <remarks>
/// <para>A literal like <c>@"C:\Projects\MyLib"</c> is an absolute path on Windows and an ordinary
/// <b>relative</b> one on Linux, where the backslashes are filename characters rather than
/// separators. Code under test that splits on <see cref="Path.DirectorySeparatorChar"/>, or
/// normalises with <c>Path.GetFullPath</c>, then behaves entirely differently — and the test fails
/// with two strings that look almost identical.</para>
///
/// <para>That is not hypothetical here. It cost six <c>MLQT.Cli</c> tests when the first Linux job
/// was added (commit <c>8110043</c>) and another eighteen across <c>ModelicaGraph.Tests</c> and
/// <c>MLQT.Services.Tests</c> when the rest of the suites started running there. In every one of
/// those cases <b>the product code was already correct</b> — it splits on both separator characters
/// and joins with the platform's own — and only the fixture assumed Windows.</para>
///
/// <para>Linked into each test project rather than copied, so the rule has one implementation. A
/// per-file <c>OperatingSystem.IsWindows() ? ... : ...</c> is the same thing written out once per
/// file, which is how the copies start.</para>
/// </remarks>
public static class TestPaths
{
    /// <summary>The filesystem root: <c>C:\</c> on Windows, <c>/</c> elsewhere.</summary>
    public static string Root { get; } = OperatingSystem.IsWindows() ? @"C:\" : "/";

    /// <summary>
    /// An absolute path built from <paramref name="segments"/>, rooted for this platform.
    /// </summary>
    /// <example>
    /// <c>TestPaths.Rooted("Projects", "MyLib", "Resources")</c> gives
    /// <c>C:\Projects\MyLib\Resources</c> on Windows and <c>/Projects/MyLib/Resources</c> on Linux.
    /// </example>
    public static string Rooted(params string[] segments) =>
        Path.Combine(Root, Path.Combine(segments));

    /// <summary>
    /// A relative path joined with the platform's separator, for the times a test needs one — a VCS
    /// reports paths relative to its root, and those have to be joined the same way the code under
    /// test joins them.
    /// </summary>
    public static string Relative(params string[] segments) => Path.Combine(segments);
}
