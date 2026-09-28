namespace MLQT.McpTester.Services;

/// <summary>
/// Where MLQT's own MCP server is, used as the first-run default for the server path box.
///
/// <para><b>Why this exists (B180).</b> The default was a literal
/// <c>C:\Projects\MLQT\MLQT.McpServer\bin\Debug\net10.0\MLQT.McpServer.exe</c> — one developer's
/// machine, so on anybody else's the tester opened pre-filled with a path that does not exist and
/// the first Connect always failed. The server's location is derivable from the tester's own, so it
/// is derived.</para>
///
/// <para><b>Nothing is assumed to be there.</b> Every candidate is probed and an empty string is
/// returned when none of them exists, because an empty box is honest and a wrong path is not. The
/// tester's whole purpose is talking to <i>any</i> stdio MCP server, so failing to find MLQT's own
/// is an ordinary outcome rather than an error.</para>
///
/// <para>Kept free of <c>File</c> and <c>AppContext</c> so it can be tested: the caller supplies the
/// base directory and the existence check. It stays in this project rather than moving to
/// <c>MLQT.Services</c> because the tester is deliberately standalone — see the compile-include
/// comment in <c>MLQT.McpTester.csproj</c> for what a project reference would drag in.</para>
/// </summary>
internal static class McpServerLocator
{
    /// <summary>The server project's assembly name, which is also its folder name in the tree.</summary>
    public const string ServerProjectName = "MLQT.McpServer";

    /// <summary>
    /// The server's file name on this platform. An apphost has no extension on Linux, and the tester
    /// runs on both — the hard-coded default was Windows-only in that respect as well.
    /// </summary>
    public static string ExecutableName =>
        OperatingSystem.IsWindows() ? ServerProjectName + ".exe" : ServerProjectName;

    /// <summary>
    /// The first candidate that exists, or an empty string when none does.
    /// </summary>
    public static string Locate(string baseDirectory, string executableName, Func<string, bool> fileExists)
    {
        foreach (var candidate in CandidatePaths(baseDirectory, executableName))
        {
            if (fileExists(candidate))
                return candidate;
        }

        return "";
    }

    /// <summary>
    /// Where to look, in order. Separate from <see cref="Locate"/> so a test can assert the order
    /// without a filesystem, and so the two layouts this has to cover are visible as a list.
    /// </summary>
    public static IEnumerable<string> CandidatePaths(string baseDirectory, string executableName)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(executableName))
            yield break;

        // AppContext.BaseDirectory carries a trailing separator, which would make every
        // GetFileName below return "".
        var root = Path.TrimEndingDirectorySeparator(baseDirectory);

        // 1. Beside the tester. This is the installer's layout — publish-tools.sh puts the shipping
        //    tools in a single tree — and it is also what anyone copying the pair into one folder
        //    would expect to work.
        yield return Path.Combine(root, executableName);

        // 2. The sibling project's build output for the same configuration and framework, which is
        //    where a developer running both from the repository has it, and the case the hard-coded
        //    path was serving:
        //      <repo>/MLQT.McpTester/bin/Debug/net10.0  ->  <repo>/MLQT.McpServer/bin/Debug/net10.0
        //    The tail is reused rather than reconstructed, so a Release build finds a Release server
        //    and a future framework bump needs no edit here.
        var framework = Path.GetFileName(root);
        var configuration = Path.GetFileName(Path.GetDirectoryName(root));
        var bin = Path.GetDirectoryName(Path.GetDirectoryName(root));
        var projectDirectory = Path.GetDirectoryName(bin);
        var containingDirectory = Path.GetDirectoryName(projectDirectory);

        if (string.IsNullOrEmpty(framework) || string.IsNullOrEmpty(configuration)
            || string.IsNullOrEmpty(containingDirectory))
            yield break;

        yield return Path.Combine(
            containingDirectory, ServerProjectName,
            Path.GetFileName(bin) ?? "bin", configuration, framework, executableName);
    }
}
