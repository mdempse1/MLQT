using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Nothing the Linux CI job builds may depend on MAUI.
/// </summary>
/// <remarks>
/// <para>The whole migration rests on one fact: <c>MLQT.Shared</c> and everything under it is
/// host-agnostic, and only the <c>MLQT</c> project is not. The design note opens by calling that
/// "the single most important fact for this design", and until now nothing checked it.</para>
///
/// <para>It would not break loudly. A MAUI reference added to a shared project builds perfectly on
/// Windows and fails on the Linux runner with a missing workload — at which point the obvious fix is
/// to install the workload in CI, which buries the problem instead of showing it. This test names
/// what actually went wrong.</para>
/// </remarks>
public class PortabilityTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    /// <summary>The projects that must build without the MAUI workload, and everything they pull in.</summary>
    private static IEnumerable<string> PortableClosure()
    {
        var root = RepositoryRoot();
        var pending = new Queue<string>([
            Path.Combine(root, "MLQT.Journeys", "MLQT.Journeys.csproj"),
            Path.Combine(root, "MLQT.TestHost", "MLQT.TestHost.csproj"),
            Path.Combine(root, "MLQT.Shared", "MLQT.Shared.csproj"),
            Path.Combine(root, "MLQT.Cli", "MLQT.Cli.csproj"),
            Path.Combine(root, "MLQT.McpServer", "MLQT.McpServer.csproj"),
        ]);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            var project = Path.GetFullPath(pending.Dequeue());
            if (!seen.Add(project) || !File.Exists(project))
                continue;

            yield return project;

            var directory = Path.GetDirectoryName(project)!;
            foreach (Match match in Regex.Matches(File.ReadAllText(project),
                         @"<ProjectReference\s+Include=""([^""]+)"""))
            {
                pending.Enqueue(Path.GetFullPath(Path.Combine(
                    directory, match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))));
            }
        }
    }

    [Fact]
    public void TheClosureIsWhatWeThinkItIs()
    {
        // Guards the walk itself: a regex that matched nothing would make every test below pass over
        // an empty list, which is the failure shape this repository keeps finding.
        var projects = PortableClosure().Select(Path.GetFileName).ToList();

        Assert.Contains("MLQT.Shared.csproj", projects);
        Assert.Contains("ModelicaParser.csproj", projects);
        Assert.True(projects.Count >= 8, $"only walked {projects.Count} projects: {string.Join(", ", projects)}");
    }

    [Fact]
    public void NoPortableProjectReferencesMaui()
    {
        var offenders = new List<string>();

        foreach (var project in PortableClosure())
        {
            var text = File.ReadAllText(project);

            // A MAUI project declares itself two ways: the workload property, and the packages.
            if (Regex.IsMatch(text, @"<UseMaui\w*>\s*true", RegexOptions.IgnoreCase)
                || text.Contains("Microsoft.Maui", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(text, @"net\d+\.\d+-(android|ios|maccatalyst|windows)"))
            {
                offenders.Add(Path.GetFileName(project));
            }
        }

        Assert.True(offenders.Count == 0,
            "These must build without the MAUI workload - the Linux journeys job installs no workload, "
            + "and if it ever needs one then something non-portable has reached a project that is "
            + "supposed to be portable: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoPortableProjectReferencesTheMauiApp()
    {
        // The other direction: MLQT is the host, and a shared project reaching back into it would
        // make the host impossible to replace, which is the entire point of phase 7.
        var offenders = PortableClosure()
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"ProjectReference[^>]*MLQT\\MLQT\.csproj"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These reference the MAUI app project, which is the one thing the migration replaces: "
            + string.Join(", ", offenders));
    }
}
