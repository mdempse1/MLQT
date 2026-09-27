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

    private static string FileAt(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts])).Replace("\r\n", "\n");

    [Fact]
    public void EveryWaitOnTheGuiSelfTestIsBounded()
    {
        // Backlog B146. Four places start the GUI to run its 16 probes and wait for it to exit, and
        // the failure they are all exposed to is the same: a host that starts and never renders does
        // not crash, it waits. Two of them were bounded and two were not, so a release could hold a
        // runner for its full six hours and report nothing about why.
        //
        // Asserted per file rather than as one search, so a failure names the script that lost it.
        var staging = FileAt("build", "publish-tools.sh");
        Assert.Contains("--self-test-timeout", staging);
        // The one place it starts the GUI, and it starts it under `timeout`. Dropping that is what
        // this guards, and it fails here rather than six hours into a release job.
        Assert.Contains("timeout \"$self_test_timeout\" \"$output/MLQT.Photino$exe\"", staging);

        var deb = FileAt("build", "package-deb.sh");
        Assert.Contains("timeout \"${MLQT_SELFTEST_TIMEOUT:-300}\"", deb);

        var release = FileAt(".github", "workflows", "release.yml");
        Assert.Contains("$gui.WaitForExit(300000)", release);      // the installed GUI, on Windows
        Assert.Contains("timeout 300 xvfb-run -a mlqt-gui", release);   // and on Linux
    }

    [Fact]
    public void EveryReleaseJobIsBounded()
    {
        // The same lesson one level up: a step can hang somewhere nobody predicted - an installer
        // waiting on a dialog, a download that never completes - and GitHub's default is six hours.
        // The build-and-test workflow's desktop-selftest job has carried a timeout since it was
        // written, with a comment saying why; the release jobs had none at all.
        var release = FileAt(".github", "workflows", "release.yml");

        var jobs = Regex.Matches(release, @"^  (?<name>[a-z][\w-]*):$", RegexOptions.Multiline)
                        .Select(m => m.Groups["name"].Value)
                        .Where(n => n is not ("push" or "pull_request" or "workflow_dispatch"))
                        .ToList();

        Assert.True(jobs.Count >= 3, $"only found {jobs.Count} jobs in release.yml: {string.Join(", ", jobs)}");

        // The version job does nothing but echo a string; the two that build are the ones that can hang.
        foreach (var job in jobs.Where(j => j != "version"))
        {
            var body = Regex.Match(release, $@"^  {Regex.Escape(job)}:\n(?<body>(?:.*\n)*?)(?=^  \S|\z)",
                                   RegexOptions.Multiline).Groups["body"].Value;

            Assert.Contains("timeout-minutes:", body);
        }
    }

    /// <summary>
    /// Every workflow file, found rather than listed, so a new one is held to the rule below
    /// without anybody remembering to add it.
    /// </summary>
    public static TheoryData<string> Workflows()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), ".github", "workflows"), "*.yml"))
            data.Add(Path.GetFileName(file));

        Assert.True(data.Count >= 3, "found fewer workflow files than this repository has");
        return data;
    }

    /// <summary>
    /// The workflow lines that run <paramref name="command"/> without being the whole of their
    /// step's <c>run:</c>. Comment lines are not commands.
    /// </summary>
    internal static List<string> CommandsNotAloneInTheirStep(string workflow, string text, string command) =>
        [.. text.Split('\n')
            .Select((line, i) => (line, number: i + 1))
            .Where(l => !l.line.TrimStart().StartsWith('#'))
            .Where(l => l.line.Contains($"dotnet {command} ")
                        && !Regex.IsMatch(l.line, $@"^\s*run: dotnet {command} "))
            .Select(l => $"{workflow}:{l.number}: {l.line.Trim()}")];

    [Theory]
    [MemberData(nameof(Workflows))]
    public void EveryDotnetCommandIsAStepOfItsOwn(string workflow)
    {
        // Backlog B362. release.yml ran its seven suites as one multi-line `run:` block, and a runner's
        // pwsh does not stop at a failing native command: the step's result is the *last* command's
        // exit code. So a tag whose parser, graph or CLI tests failed still built and published the
        // installer, provided MLQT.Shared.Tests passed. One `dotnet test` per step is what
        // build-and-test.yml has always done, and a step's own exit code cannot be the wrong one.
        //
        // Backlog B370: build-and-test.yml had the same weakness in its builds - nine projects in one
        // step, so a middle one that failed to compile while a stale binary of it existed did not stop
        // the job. Mostly masked by the --no-build test steps failing on a missing assembly, which is
        // exactly the case a stale binary defeats. Restore and publish are the same kind of step.
        var text = FileAt(".github", "workflows", workflow);

        var offenders = new[] { "test", "build", "restore", "publish" }
            .SelectMany(command => CommandsNotAloneInTheirStep(workflow, text, command))
            .ToList();

        Assert.True(offenders.Count == 0,
            "a dotnet command that is not the whole of its step's run: - only the last command's exit "
            + "code decides a multi-line step:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheStepCheckSeesACommandInsideAMultiLineStep()
    {
        // The check above is a pattern over text, and a pattern that stopped matching would pass every
        // workflow trivially. This is the shape build-and-test.yml had before B370.
        const string before = """
                  - name: Build library projects
                    run: |
                      dotnet build ModelicaParser/ModelicaParser.csproj -c Release --no-restore
                      # dotnet build in a comment is not a command
                      dotnet build ModelicaGraph/ModelicaGraph.csproj -c Release --no-restore

                  - name: Restore
                    run: dotnet restore MLQT.slnx
            """;
        var text = before.Replace("\r\n", "\n");

        Assert.Equal(2, CommandsNotAloneInTheirStep("x.yml", text, "build").Count);
        Assert.Empty(CommandsNotAloneInTheirStep("x.yml", text, "restore"));
    }

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
