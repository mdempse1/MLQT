using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// B184 — a <c>--changed-from</c> run loads the whole library and checks only what changed.
///
/// <para><b>The whole library is still loaded</b>, and has to be: a class cannot be checked without
/// its base classes, and a type written as <c>SI.Length</c> cannot be resolved without the library
/// that defines it. What is skipped is applying the rules to models the change did not touch, which
/// on a real library is most of a CI run.</para>
///
/// <para><b>Except when the run asks for whole-library numbers.</b> Coverage over the dozen models a
/// commit touched is not that library's coverage, and a ratchet recording it would move the baseline
/// to a figure nothing can be compared against — so <c>--metrics</c>, <c>--min-coverage</c> and
/// <c>--coverage-ratchet</c> opt the run back into checking everything, and say so.</para>
/// </summary>
public class ChangedFromChecksLessTests : IDisposable
{
    private readonly TempWorkspace _workspace = new("mlqt-changed-from");
    private readonly string _baseRev;

    /// <summary>Five classes, committed, with one of them then edited.</summary>
    public ChangedFromChecksLessTests()
    {
        for (var i = 0; i < 5; i++)
            _workspace.Write($"Model{i}.mo", $"model Model{i}\n  parameter Real x{i} = 1.0;\nend Model{i};\n");
        _workspace.WithSettings("""{ "ParameterHasDescription": true }""");

        LibGit2Sharp.Repository.Init(_workspace.Root);
        using (var repo = new LibGit2Sharp.Repository(_workspace.Root))
        {
            LibGit2Sharp.Commands.Stage(repo, "*");
            var sig = new LibGit2Sharp.Signature("t", "t@e.com", DateTimeOffset.Now);
            repo.Commit("init", sig, sig, new LibGit2Sharp.CommitOptions());
            _baseRev = repo.Head.Tip.Sha;
        }

        _workspace.Write("Model2.mo",
            "model Model2\n  parameter Real x2 = 1.0;\n  // touched\nend Model2;\n");
    }

    public void Dispose() => _workspace.Dispose();

    private string Path => _workspace.Root;

    /// <summary>The "N class(es) checked" figure the run reports.</summary>
    private static int CheckedCount(string stderr, string stdout)
    {
        // The summary's trailing count — "across N model(s)." when there are findings, and
        // "No findings in N model(s)." when there are none.
        var match = System.Text.RegularExpressions.Regex.Match(stdout + stderr, @"(\d+) model\(s\)\.");
        Assert.True(match.Success, $"no model count in output:\n{stdout}\n{stderr}");
        return int.Parse(match.Groups[1].Value);
    }

    [Fact]
    public void OnlyTheChangedModelsAreChecked()
    {
        var full = Cli.Run("check", Path, "--no-color");
        var narrowed = Cli.Run("check", Path, "--changed-from", _baseRev, "--no-color");

        var fullCount = CheckedCount(full.stderr, full.stdout);
        var narrowedCount = CheckedCount(narrowed.stderr, narrowed.stdout);

        Assert.True(fullCount >= 5, $"the fixture should have several classes; a full run checked {fullCount}");
        Assert.True(narrowedCount < fullCount,
            $"a changed-from run checked {narrowedCount} of {fullCount} — it should check fewer, not all");
    }

    [Fact]
    public void AskingForMetricsChecksEverythingAndSaysSo()
    {
        // The decision, made explicit: coverage measures the library, so it opts out of the narrowing
        // rather than quietly reporting a number derived from a handful of models.
        var full = Cli.Run("check", Path, "--no-color");
        var withMetrics = Cli.Run("check", Path, "--changed-from", _baseRev,
            "--metrics-out", _workspace.PathTo("metrics.json"), "--no-color");

        Assert.Equal(CheckedCount(full.stderr, full.stdout),
                     CheckedCount(withMetrics.stderr, withMetrics.stdout));
        Assert.Contains("measures the whole library, so every model is checked", withMetrics.stderr);
    }

    [Fact]
    public void ADiffThatCannotBeTakenStopsTheRun()
    {
        // The narrowing must not turn a broken diff into "nothing changed", which would be a silent
        // pass over an unchecked library — the worst outcome this option has.
        var (code, _, stderr) = Cli.Run("check", Path, "--changed-from", "no-such-ref", "--no-color");

        Assert.Equal(2, code);
        Assert.Contains("error", stderr);
    }

    [Fact]
    public void TheChangedModelIsStillReportedOn()
    {
        // Checking less must not mean finding less in what was checked: Model2 has an undescribed
        // parameter, and the narrowed run still has to say so.
        var narrowed = Cli.Run("check", Path, "--changed-from", _baseRev, "--format", "json", "--no-color");

        Assert.Contains("Model2", narrowed.stdout);
    }

    [Fact]
    public void TheUnchangedModelsAreNotReportedOn()
    {
        // The other half, and the one that proves the narrowing is real rather than cosmetic: Model0
        // has the same undescribed parameter and must not appear, because it was not checked.
        var narrowed = Cli.Run("check", Path, "--changed-from", _baseRev, "--format", "json", "--no-color");

        Assert.DoesNotContain("Model0", narrowed.stdout);
    }
}
