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
    public void EveryPublishInTheStagingScriptCarriesTheVersion()
    {
        // The failure this was written for: a publish step nobody remembered to pass the version to,
        // producing an artefact that is confidently wrong.
        //
        // It used to read release.yml, because that is where the publishes were. They have moved into
        // build/publish-tools.ps1, and this test found that out by failing when the workflow was
        // converted - which is the correct outcome for a guard whose subject moved, and better than
        // one that kept passing over an empty search.
        //
        // Read with newlines normalised, and the patterns below written with \n rather than
        // a literal line break. They used to carry the line ending of *this* file, so they only
        // matched while both files agreed about it - and a tree where they did not failed with
        // "found 0 invocations", which reads as a workflow that has stopped calling the script
        // rather than as what it is.
        var staging = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "build", "publish-tools.ps1")).Replace("\r\n", "\n");

        var publishes = Regex.Matches(staging,
            @"dotnet publish(?<args>(.|\n)*?)(?=\n\s*if|\n\s*\})");

        Assert.True(publishes.Count >= 1,
            "no dotnet publish found in publish-tools.ps1; the format may have changed");

        Assert.All(publishes, m =>
            Assert.Contains("-p:Version=", m.Groups["args"].Value, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryReleaseJobPassesTheVersionToTheStagingScript()
    {
        // The other end of the same chain. The script defaults to 0.0.0-dev when nothing passes a
        // version, so a job that forgot would publish, package and upload a release artefact calling
        // itself a development build - and every check in the script would pass, because they compare
        // what the tools report against what was asked for, and both would be 0.0.0-dev.
        var workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"))
            .Replace("\r\n", "\n");

        // Anchored on "pwsh build/publish-tools.ps1", the actual invocation. A looser pattern matched
        // a *comment* that mentions the script by name, and reported it as a job that had forgotten
        // the version.
        var invocations = Regex.Matches(workflow,
            @"pwsh build/publish-tools\.ps1(?<args>(.|\n)*?)(?=\n\s*\n|\n\s*-\s+name:)");

        Assert.True(invocations.Count >= 2,
            $"found {invocations.Count} publish-tools.ps1 invocations in release.yml; expected one per platform");

        Assert.All(invocations, m =>
            Assert.Contains("-Version", m.Groups["args"].Value, StringComparison.Ordinal));
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
