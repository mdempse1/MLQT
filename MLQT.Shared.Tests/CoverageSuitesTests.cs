using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The coverage gate's suite list, and the CI job that has to build it.
/// </summary>
/// <remarks>
/// <para><c>build/check-coverage.ps1</c> names the suites it measures and runs each with
/// <c>--no-build</c>; the <c>code-coverage</c> job in <c>build-and-test.yml</c> builds them
/// beforehand. Two lists, one rule, and nothing holding them together — the shape this backlog has
/// named more times than any other.</para>
///
/// <para>It cost a build. 7a-5 added <c>MLQT.Shared.Tests</c> to the gate and not to the workflow, so
/// the job would have failed with "expected 7 coverage reports, found 6" — and it did not say so for
/// three commits, because the jobs it depends on kept failing first and it never ran at all. A gate
/// that cannot run is not a gate.</para>
///
/// <para>Deliberately asserts in both directions. A suite built by CI but not measured is wasted
/// build time and a misleading job name; a suite measured but not built fails the run.</para>
/// </remarks>
public class CoverageSuitesTests
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

    private static string Normalised(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n");

    /// <summary>The suites <c>check-coverage.ps1</c> measures.</summary>
    private static List<string> SuitesInTheScript()
    {
        // Line endings normalised: these files are CRLF in the working tree and the patterns below
        // anchor on newlines.
        var script = Normalised(Path.Combine(RepositoryRoot(), "build", "check-coverage.ps1"));

        // The $suites array, then the Project = '...' entry in each row.
        var block = Regex.Match(script, @"\$suites\s*=\s*@\((?<body>.*?)\n\)", RegexOptions.Singleline);
        Assert.True(block.Success, "could not find the $suites array in check-coverage.ps1");

        return [.. Regex.Matches(block.Groups["body"].Value, @"Project\s*=\s*'([^']+)'")
                        .Select(m => m.Groups[1].Value)];
    }

    /// <summary>The test projects the coverage job builds.</summary>
    private static List<string> SuitesBuiltByTheCoverageJob()
    {
        var workflow = Normalised(
            Path.Combine(RepositoryRoot(), ".github", "workflows", "build-and-test.yml"));

        // The code-coverage job, up to the next top-level job.
        var job = Regex.Match(workflow, @"^  code-coverage:\n(?<body>(?:.*\n)*?)(?=^  \S|\z)",
                              RegexOptions.Multiline);
        Assert.True(job.Success, "could not find the code-coverage job in build-and-test.yml");

        return [.. Regex.Matches(job.Groups["body"].Value, @"dotnet build (\S+\.Tests) ")
                        .Select(m => m.Groups[1].Value)];
    }

    [Fact]
    public void TheChecksCanSeeBothLists()
    {
        // Neither side is allowed to come back empty: a regex that stopped matching would make every
        // assertion below trivially true, which is the failure this suite exists to prevent
        // elsewhere and would be embarrassing here.
        Assert.True(SuitesInTheScript().Count >= 6, "found too few suites in check-coverage.ps1");
        Assert.True(SuitesBuiltByTheCoverageJob().Count >= 6, "found too few builds in the coverage job");
    }

    [Fact]
    public void EverySuiteTheGateMeasures_IsBuiltByTheJob()
    {
        // The direction that breaks the build. check-coverage.ps1 runs with --no-build, so a suite
        // the job did not build produces no report, and a missing report is not "one suite skipped"
        // - the script fails the whole run, by design, because a missing report reads as 0%.
        var missing = SuitesInTheScript().Except(SuitesBuiltByTheCoverageJob()).ToList();

        Assert.True(missing.Count == 0,
            "check-coverage.ps1 measures these, and the code-coverage job does not build them: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void EverySuiteTheJobBuilds_IsMeasuredByTheGate()
    {
        var extra = SuitesBuiltByTheCoverageJob().Except(SuitesInTheScript()).ToList();

        Assert.True(extra.Count == 0,
            "the code-coverage job builds these and the gate does not measure them: "
            + string.Join(", ", extra));
    }

    [Fact]
    public void TheSuiteListHasNoDuplicates()
    {
        var suites = SuitesInTheScript();

        Assert.Equal(suites.Count, suites.Distinct().Count());
    }
}
