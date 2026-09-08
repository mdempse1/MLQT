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

    /// <summary>The hosts that must never depend on MAUI, whatever else they depend on.</summary>
    /// <remarks>
    /// These are not in the portable closure — nothing references an executable — so the test above
    /// cannot see them, and both were MAUI applications until phase 7b. Naming them explicitly is the
    /// point: the migration is only finished when nothing but <c>MLQT</c> itself needs the workload,
    /// and a regression here would be somebody adding a MAUI package back to solve a problem that has
    /// a Photino answer.
    /// </remarks>
    public static TheoryData<string> MauiFreeHosts() => new() { "MLQT.Photino", "MLQT.McpTester" };

    [Theory]
    [MemberData(nameof(MauiFreeHosts))]
    public void TheNonMauiHostsStayNonMaui(string project)
    {
        var path = Path.Combine(RepositoryRoot(), project, project + ".csproj");
        Assert.True(File.Exists(path), $"{project} has no project file at {path}");

        var text = File.ReadAllText(path);

        Assert.False(Regex.IsMatch(text, @"<UseMaui\w*>\s*true", RegexOptions.IgnoreCase),
            $"{project} declares UseMaui");
        Assert.False(text.Contains("Microsoft.Maui", StringComparison.OrdinalIgnoreCase),
            $"{project} references a Microsoft.Maui package");
        // The declared target framework, not the file's text: McpTester's own csproj comment says what
        // it used to target, and matching that would fail the project for explaining itself.
        var frameworks = Regex.Matches(text, @"<TargetFrameworks?>([^<]+)</TargetFrameworks?>")
                              .SelectMany(m => m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                              .Select(f => f.Trim())
                              .ToList();

        Assert.NotEmpty(frameworks);
        Assert.All(frameworks, f =>
            Assert.False(Regex.IsMatch(f, @"-(android|ios|maccatalyst|windows)"),
                $"{project} targets {f}, which pulls the workload back in"));
    }

    [Fact]
    public void OnlyTheMauiAppStillUsesTheWorkload()
    {
        // The number that says how far the migration has got, asserted so that it can only go down
        // deliberately. Two projects needed the workload before 7b-1; one does now; 7b-8 makes it none.
        var users = Directory.EnumerateFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
                             .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                             .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                             .Where(p => Regex.IsMatch(File.ReadAllText(p), @"<UseMaui\w*>\s*true", RegexOptions.IgnoreCase))
                             .Select(Path.GetFileNameWithoutExtension)
                             .Order()
                             .ToList();

        Assert.Equal(["MLQT"], users);
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
