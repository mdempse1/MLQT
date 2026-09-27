using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The filters that run the simulation-tool suites without the tool, held to being one filter each
/// and to the trait they select on (B399).
/// </summary>
/// <remarks>
/// <para><c>DymolaInterface.Tests</c> and <c>OpenModelicaInterface.Tests</c> drive a live Dymola and
/// omc, which no runner has, so CI ran neither - and with them, the large part of each that needs no
/// tool at all. The classes that do need one now carry <c>[Trait("Requires", "Dymola")]</c> or
/// <c>[Trait("Requires", "OpenModelica")]</c>, and CI runs the rest with a filter on that trait: by
/// what a class needs, never by what it is called, which is the lesson <see cref="SvnTestFilterTests"/>
/// was written for (B266).</para>
///
/// <para>The filter is written in four places - both CI jobs, <c>run-all-tests.ps1</c> and, since
/// B438, <c>check-coverage.ps1</c> - and the
/// trait in two test projects. A filter that drifts from the trait selects nothing, or everything:
/// the first reads as a suite with nothing to run, the second starts a Dymola on a runner that has
/// none. Each suite's own <c>ToolTraitTests</c> holds its classes to the trait; this holds the
/// filters to it.</para>
/// </remarks>
public class LiveToolTestFilterTests
{
    internal const string DymolaFilter = "Requires!=Dymola";
    internal const string OpenModelicaFilter = "Requires!=OpenModelica";

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

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts])).Replace("\r\n", "\n");

    /// <summary>The body of one job in build-and-test.yml, from its key to the next job.</summary>
    private static string Job(string name)
    {
        var match = Regex.Match(Read(".github", "workflows", "build-and-test.yml"),
                                $@"^  {Regex.Escape(name)}:\n(?<body>(?:.*\n)*?)(?=^  \S|\z)",
                                RegexOptions.Multiline);

        Assert.True(match.Success, $"no job called {name} in build-and-test.yml");
        return match.Groups["body"].Value;
    }

    public static TheoryData<string, string, string> Suites() => new()
    {
        { "DymolaInterface.Tests", "Dymola", DymolaFilter },
        { "OpenModelicaInterface.Tests", "OpenModelica", OpenModelicaFilter },
    };

    [Theory]
    [MemberData(nameof(Suites))]
    public void TheFilterIsTheNegationOfTheTraitTheSuiteMarksItsToolClassesWith(string suite, string tool, string filter)
    {
        Assert.Equal($"Requires!={tool}", filter);

        // The trait, as the test project writes it. Found in the sources rather than by loading the
        // assembly, which this suite does not reference.
        var marked = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), suite), "*.cs")
            .Count(f => File.ReadAllText(f).Contains($"[Trait(\"Requires\", \"{tool}\")]"));

        Assert.True(marked >= 4, $"{suite} has only {marked} classes marked as requiring {tool}");
    }

    [Theory]
    [InlineData("build-libraries")]
    [InlineData("linux-tests")]
    public void BothCiJobsRunBothSuitesWithTheirFilter(string job)
    {
        var body = Job(job);

        Assert.Contains($"dotnet test DymolaInterface.Tests -c Release --no-build --filter \"{DymolaFilter}\"", body);
        Assert.Contains($"dotnet test OpenModelicaInterface.Tests -c Release --no-build --filter \"{OpenModelicaFilter}\"", body);
    }

    [Theory]
    [InlineData("DymolaInterface.Tests")]
    [InlineData("OpenModelicaInterface.Tests")]
    public void NoWorkflowRunsASimulationToolSuiteUnfiltered(string suite)
    {
        // The failure the filter exists to prevent: the suite run whole, starting a tool the runner
        // does not have and failing on every class that needs it.
        var unfiltered = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), ".github", "workflows"), "*.yml")
            .SelectMany(f => Read(".github", "workflows", Path.GetFileName(f)).Split('\n')
                .Where(l => l.Contains($"dotnet test {suite} ") && !l.Contains("--filter \"Requires!="))
                .Select(l => $"{Path.GetFileName(f)}: {l.Trim()}"))
            .ToList();

        Assert.True(unfiltered.Count == 0, "run without the tool filter:\n" + string.Join("\n", unfiltered));
    }

    [Fact]
    public void TheLocalScriptsCoreRunUsesTheSameFilters()
    {
        // -CoreOnly is "would CI be green?", so it runs what CI runs: these suites filtered, not skipped.
        var script = Read("build", "run-all-tests.ps1");

        Assert.Contains($"CoreFilter   = '{DymolaFilter}'", script);
        Assert.Contains($"CoreFilter   = '{OpenModelicaFilter}'", script);
        Assert.Contains("$s.Filter = $s.CoreFilter", script);
    }

    [Fact]
    public void TheCoverageGateMeasuresBothSuitesWithTheirFilter()
    {
        // Backlog B438. The two assemblies are under the ratchet, measured from what CI can run. The
        // gate runs its suites itself, so it is a fourth place the filter is written: without it the
        // coverage job would start Dymola and omc on a runner that has neither, and fail.
        var script = Read("build", "check-coverage.ps1");

        Assert.Contains($"@{{ Project = 'DymolaInterface.Tests';       Filter = '{DymolaFilter}' }}", script);
        Assert.Contains($"@{{ Project = 'OpenModelicaInterface.Tests'; Filter = '{OpenModelicaFilter}' }}", script);

        // And both assemblies carry a bar, or their suites would run for nothing.
        var assemblies = Read("build", "CoverageAssemblies.ps1");
        Assert.Matches(@"(?m)^\s*'DymolaInterface'\s*=\s*80\.0", assemblies);
        Assert.Matches(@"(?m)^\s*'OpenModelicaInterface'\s*=\s*80\.0", assemblies);
    }

    [Fact]
    public void TheMutationCampaignLeavesBothToolAssembliesOut()
    {
        // Backlog B453. Stryker's MTP runner cannot be given the filter - its test-case-filter is
        // read by the VSTest runner only - so mutating either assembly runs its suite whole and
        // starts the tool. Adding one to -All's list without the filter reaching Stryker would start
        // Dymola or omc for every mutant.
        var script = Read("build", "run-mutation.ps1");
        var list = Regex.Match(script, @"\$MutationProjects = @\((?<body>[^)]*)\)");

        Assert.True(list.Success, "no $MutationProjects list in run-mutation.ps1");
        Assert.Contains("'ModelicaParser'", list.Groups["body"].Value);
        Assert.DoesNotContain("DymolaInterface", list.Groups["body"].Value);
        Assert.DoesNotContain("OpenModelicaInterface", list.Groups["body"].Value);
    }

    [Fact]
    public void EveryCopyOfATraitFilterIsOneOfTheTwo()
    {
        // A third spelling - "Requires!=Omc", a stray space - selects no class, so the suite runs whole.
        var files = new[]
        {
            Read(".github", "workflows", "build-and-test.yml"),
            Read(".github", "workflows", "release.yml"),
            Read("build", "run-all-tests.ps1"),
            Read("build", "check-coverage.ps1"),
        };

        var filters = files.SelectMany(f => Regex.Matches(f, @"Requires\s*!=\s*[A-Za-z]+").Select(m => m.Value))
                           .Distinct()
                           .ToList();

        Assert.NotEmpty(filters);
        Assert.All(filters, f => Assert.Contains(f, new[] { DymolaFilter, OpenModelicaFilter }));
    }
}
