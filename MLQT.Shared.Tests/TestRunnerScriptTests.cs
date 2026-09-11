using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The decisions <c>build/run-all-tests.ps1</c> carries that nothing else can check.
/// </summary>
/// <remarks>
/// <para>Backlog B135. Playwright ships no browser build for an Ubuntu newer than 24.04 and refuses
/// to install one — "Playwright does not support chromium on ubuntu26.04-x64" — so on the Linux
/// development machine the journey suite could not run at all, and failed in a way that reads as a
/// broken suite rather than a missing browser. The script now detects that and sets
/// <c>PLAYWRIGHT_HOST_PLATFORM_OVERRIDE</c>, under which all 51 journeys pass under Chromium.</para>
///
/// <para>The decision itself is a PowerShell function taking the contents of
/// <c>/etc/os-release</c> rather than reading it, so it can be exercised for a distribution the
/// machine is not — which is how the first version was found to be wrong. It built the override name
/// from a <c>[version]</c>, and <c>[version]'24.04'</c> renders as <c>24.4</c>: the name would have
/// been <c>ubuntu24.4-x64</c>, which Playwright has never heard of, and the workaround would have
/// done nothing at all while looking correct. Seven cases were run against the fixed version —
/// 26.04, 25.10, 24.04, 22.04, an unquoted VERSION_ID, Debian, and no os-release — and all seven
/// answer correctly.</para>
///
/// <para>What is asserted here is what a C# test can reach: that the mechanism is still in the
/// script, and that the value it is built from is still a string. Running the function itself would
/// mean shelling out to <c>pwsh</c>, which this suite cannot depend on — B135 also records that the
/// machine in question has no <c>pwsh</c> installed, so the test would fail there for a reason that
/// has nothing to do with what it is testing.</para>
/// </remarks>
public class TestRunnerScriptTests
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

    // Newlines normalised, because a pattern matched against a build file must not depend on the two
    // files agreeing about line endings - see ReleaseVersionTests for what that costs.
    private static string Script() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "build", "run-all-tests.ps1"))
            .Replace("\r\n", "\n");

    [Fact]
    public void TheJourneySuiteGetsPlaywrightsPlatformOverrideOnAnUbuntuItDoesNotSupport()
    {
        var script = Script();

        Assert.Contains("PLAYWRIGHT_HOST_PLATFORM_OVERRIDE", script);
        Assert.Contains("function Get-PlaywrightPlatformOverride", script);

        // Not set when one is already in the environment: a person debugging a browser problem sets
        // it by hand, and having the script overwrite that silently is worse than not helping.
        Assert.Contains("-not $env:PLAYWRIGHT_HOST_PLATFORM_OVERRIDE", script);
    }

    [Fact]
    public void TheNewestSupportedUbuntuIsAStringAndTheOverrideIsBuiltFromIt()
    {
        var script = Script();

        // **The defect this is really about.** The override is a platform *name*, and the release
        // number is half of it. Held as a [version] it loses the trailing zero - 24.04 renders as
        // 24.4 - so the script would ask for ubuntu24.4-x64, Playwright would reject it, and the
        // journeys would fail exactly as they did before the workaround existed.
        var assignment = Regex.Match(script, @"\$NewestPlaywrightUbuntu\s*=\s*(?<value>.+)");
        Assert.True(assignment.Success, "run-all-tests.ps1 no longer names a newest supported Ubuntu");

        var value = assignment.Groups["value"].Value.Trim();
        Assert.Matches(@"^'[0-9]+\.[0-9]+'$", value);

        // And the name is composed from that variable rather than written out again, so bumping it
        // when Playwright adds a platform is the whole change.
        Assert.Contains("\"ubuntu$NewestSupported-x64\"", script);
    }
}
