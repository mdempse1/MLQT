using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Everything a release ships reports the same version, and that version comes from the tag.
/// </summary>
/// <remarks>
/// <para>Phase 7b-7. Three tools now ship inside one installer per platform, so "which version is
/// this?" needs one answer. It had three: <c>MLQT.Cli.csproj</c> pinned <c>0.1.0</c>, everything else
/// took the SDK default of <c>1.0.0</c>, and <c>release.yml</c> passed <c>-p:Version</c> to the two
/// CLI commands and to nothing else. So every release ever tagged shipped a CLI that agreed with the
/// tag, an MCP server that said 1.0.0, and a GUI whose version line said 1.0.0.</para>
///
/// <para><b>Nothing could have noticed.</b> The number is only wrong in the published artefact, and it
/// is wrong in a way that looks like a plausible version rather than like a fault. The two tests here
/// are the two halves of it: one reads the workflow, because that is where the omission was, and one
/// reads the assemblies, because a project that pins its own version would satisfy the first and
/// still diverge.</para>
/// </remarks>
public class ReleaseVersionTests
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

    [Fact]
    public void EveryPublishAndPackInTheReleaseWorkflowCarriesTheVersion()
    {
        // The failure exactly as it happened: a publish step that nobody remembered to pass the
        // version to, producing an artefact that is confidently wrong. Written to read whatever
        // commands the workflow has rather than a list of the ones it has today, so splitting it into
        // a Windows job and a Linux job for 7b-7 cannot quietly drop one.
        var workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));

        // A publish or pack command and everything up to the blank line or the next step that ends it.
        var commands = Regex.Matches(
            workflow,
            @"dotnet\s+(publish|pack)\b(?<args>(.|\n)*?)(?=\n\s*\n|\n\s*-\s+name:)",
            RegexOptions.None);

        Assert.True(commands.Count >= 3,
            $"found only {commands.Count} publish/pack commands in release.yml; the format may have changed");

        var missing = commands
            .Where(c => !c.Groups["args"].Value.Contains("-p:Version="))
            .Select(c => c.Value.Split('\n')[0].Trim())
            .ToList();

        Assert.True(missing.Count == 0,
            "release.yml publishes without a version, so the artefact ships whatever the SDK defaults to:"
            + string.Concat(missing.Select(m => Environment.NewLine + "  " + m)));
    }

    [Fact]
    public void NoProjectPinsItsOwnVersion()
    {
        // The other half. A csproj that sets Version wins over Directory.Build.props and over the
        // tag, so it would ship a number of its own however carefully the workflow was written -
        // which is what MLQT.Cli did, and why the CLI was the one tool that looked right.
        var offenders = Directory
            .EnumerateFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"<Version>|<VersionPrefix>|<InformationalVersion>"))
            .Select(p => Path.GetRelativePath(RepositoryRoot(), p))
            .ToList();

        Assert.True(offenders.Count == 0,
            "these projects set a version of their own; it belongs in Directory.Build.props so the tag "
            + "reaches everything:" + string.Concat(offenders.Select(o => Environment.NewLine + "  " + o)));
    }

    [Fact]
    public void TheRepositoryHasOneVersionAndEveryAssemblyAgrees()
    {
        // Read from the built assemblies rather than the project files, because this is the property
        // that actually ships: ToolInfo reports it from `mlqt`, MainLayout shows it to the user, and
        // every SARIF document names it. Assemblies from one build of one repository must agree.
        var assemblies = new[]
        {
            typeof(MLQT.Shared.HostAssetManifest).Assembly,
            typeof(MLQT.Services.LoggingService).Assembly,
            typeof(ModelicaGraph.DirectedGraph).Assembly,
            typeof(RevisionControl.SvnToolLocator).Assembly,
        };

        var versions = assemblies
            .Select(a => new
            {
                Name = a.GetName().Name,
                Version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            })
            .ToList();

        Assert.All(versions, v => Assert.False(string.IsNullOrWhiteSpace(v.Version),
            $"{v.Name} carries no informational version at all"));

        // The "+commithash" suffix SourceLink appends is not part of the version anyone reads - and
        // MainLayout strips it before display - so it is stripped here too.
        var distinct = versions
            .Select(v => v.Version!.Split('+')[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(distinct.Count == 1,
            "assemblies from one build disagree about the version: "
            + string.Join(", ", versions.Select(v => $"{v.Name}={v.Version}")));
    }

    [Fact]
    public void TheVersionIsNotTheSdkDefault()
    {
        // 1.0.0 is what every project reports when nothing sets a version - which is exactly the state
        // this phase found and fixed. A local build should say 0.0.0-dev and a CI build the tag; both
        // are true statements, and 1.0.0 is the one that is not, because it means Directory.Build.props
        // is not being picked up and the tag is going nowhere.
        //
        // Written as "not the default" rather than "equals 0.0.0-dev" so it holds in CI too. The first
        // draft returned early unless the version was 0.0.0-dev and then asserted it was - a test that
        // could not fail.
        var version = typeof(MLQT.Shared.HostAssetManifest).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

        Assert.NotEqual("1.0.0", version);
    }
}
