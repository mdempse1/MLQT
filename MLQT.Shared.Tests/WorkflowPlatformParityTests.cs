using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// That the test suites run on both platforms, and the journeys with them.
/// </summary>
/// <remarks>
/// <para>Phase 7's deliverable is a Linux UI, and until 2026-09-08 the only thing CI ran on Linux was
/// the browser journeys: all nine suites ran on <c>windows-latest</c> and nowhere else, while the
/// journeys ran on <c>ubuntu-latest</c> and nowhere else. Each platform tested what the other did
/// not.</para>
///
/// <para>It was not hypothetical. The first Linux job this repository ever had found six
/// <c>MLQT.Cli</c> tests built around a literal Windows drive path within a day of being added
/// (commit <c>8110043</c>, "The Linux job found them, which is what it is for"), and there is no
/// reason to think the six suites that had never run there were cleaner.</para>
///
/// <para>Two job definitions, one intent — the shape this backlog keeps naming. A suite added to the
/// Windows job and forgotten on the Linux one would go back to being tested on one platform, and
/// nothing but this would say so.</para>
/// </remarks>
public class WorkflowPlatformParityTests
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

    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "build-and-test.yml"))
            .Replace("\r\n", "\n");

    /// <summary>The body of one job, from its key to the next top-level job.</summary>
    private static string Job(string name)
    {
        var match = Regex.Match(Workflow(), $@"^  {Regex.Escape(name)}:\n(?<body>(?:.*\n)*?)(?=^  \S|\z)",
                                RegexOptions.Multiline);

        Assert.True(match.Success, $"no job called {name} in build-and-test.yml");
        return match.Groups["body"].Value;
    }

    /// <summary>The suites a job actually runs, as opposed to merely builds.</summary>
    private static List<string> SuitesRunBy(string job) =>
        [.. Regex.Matches(Job(job), @"dotnet test (\S+)")
                 .Select(m => m.Groups[1].Value)
                 .Distinct()];

    [Fact]
    public void TheChecksCanSeeBothJobs()
    {
        // Neither list may come back empty: a regex that stopped matching would make the parity
        // assertion below trivially true, which is precisely the failure mode this repository has
        // been caught by twice.
        Assert.True(SuitesRunBy("build-libraries").Count >= 6, "found too few suites in build-libraries");
        Assert.True(SuitesRunBy("linux-tests").Count >= 6, "found too few suites in linux-tests");
    }

    [Fact]
    public void EverySuiteTheWindowsJobRuns_AlsoRunsOnLinux()
    {
        // The assertion that matters. Phase 7b ports the desktop host to Linux; a suite that has
        // only ever run on Windows is a suite whose Linux behaviour is unknown at exactly the moment
        // it starts mattering.
        var missing = SuitesRunBy("build-libraries").Except(SuitesRunBy("linux-tests")).ToList();

        Assert.True(missing.Count == 0,
            "these run on Windows and not on Linux: " + string.Join(", ", missing));
    }

    [Fact]
    public void EverySuiteTheLinuxJobRuns_AlsoRunsOnWindows()
    {
        // The other direction. Windows is the platform the product ships on today, so a suite that
        // runs only on Linux would be the worse asymmetry of the two.
        var missing = SuitesRunBy("linux-tests").Except(SuitesRunBy("build-libraries")).ToList();

        Assert.True(missing.Count == 0,
            "these run on Linux and not on Windows: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheSvnExclusionIsTheSameOnBothPlatforms()
    {
        // The two jobs have to measure the same thing or their results are not comparable. The SVN
        // integration tests need a working copy and a server no runner has, and check-coverage.ps1
        // excludes them the same way for the same reason.
        const string filter = @"--filter ""FullyQualifiedName!~Svn""";

        Assert.Contains(filter, Job("build-libraries"));
        Assert.Contains(filter, Job("linux-tests"));
    }

    [Fact]
    public void TheJourneysRunOnBothPlatforms()
    {
        // They are the only end-to-end coverage of the shared UI - the only thing that would catch a
        // component rendering the wrong thing - and they used to run on Linux alone, which left the
        // platform the product actually ships on uncovered by them.
        var journeys = Job("ui-journeys");

        Assert.Contains("ubuntu-latest", journeys);
        Assert.Contains("windows-latest", journeys);
    }

    [Fact]
    public void NeitherPortableJobInstallsTheMauiWorkload()
    {
        // Stated as a test because it is the point of those jobs, not a detail of them: if one ever
        // needs the workload, something non-portable has reached a project that is supposed to be
        // portable, and that is the finding rather than a reason to install it.
        foreach (var job in new[] { "linux-tests", "ui-journeys", "desktop-selftest" })
            Assert.DoesNotContain("workload install", Job(job));
    }

    [Fact]
    public void TheDesktopSelfTestRunsOnBothPlatforms()
    {
        // Phase 7b-6. The desktop host is the thing the migration replaces, and a parity gate that
        // ran on one platform would say nothing about the one the phase exists to deliver.
        var selfTest = Job("desktop-selftest");

        Assert.Contains("ubuntu-latest", selfTest);
        Assert.Contains("windows-latest", selfTest);
    }

    [Fact]
    public void BothDesktopSelfTestLegsCaptureAndCompare()
    {
        // Unlike the journeys, this job's capture step is NOT one definition: the two platforms need
        // different shells (xvfb-run against a bare .exe), so there is a step per OS and a leg can
        // lose its capture while the job stays green - the comparison would then run against the
        // committed record and pass, proving nothing about this runner. The two `if:` guards are what
        // makes that possible, so they are what is asserted.
        var selfTest = Job("desktop-selftest");

        Assert.Contains("Capture the self-test report (Linux)", selfTest);
        Assert.Contains("Capture the self-test report (Windows)", selfTest);
        Assert.Contains("MLQT_SELFTEST", selfTest);
        Assert.Contains("DesktopHostConformanceTests", selfTest);
    }
}
