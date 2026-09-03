using System.Text.Json;
using MLQT.Cli;
using MLQT.Services.Checking;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// The pull-request review body. The one rule everything here turns on: GitHub takes a review comment
/// only on a line that is in the pull request's diff, and refusing one comment fails the whole review.
/// So a comment must never be emitted for a line the change did not touch — not "rarely", never.
/// </summary>
public class ReviewFindingFormatterTests
{
    private const string Repo = @"C:\repo";
    private static readonly string LibraryFile = Path.Combine(Repo, "Lib", "package.mo");

    private static Finding Finding(
        string model = "Lib.M",
        string message = "The class is missing a description string",
        RuleSeverity severity = RuleSeverity.Warning,
        int line = 1) =>
        new()
        {
            RuleId = RuleIds.ClassDescription,
            ModelId = model,
            Message = message,
            Severity = severity,
            LineNumber = line,
        };

    /// <summary>A diff that says exactly these lines of the library file changed.</summary>
    private static ChangedLineResult Diff(params int[] lines) =>
        new(true, Repo,
            new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase)
            {
                [LibraryFile] = new HashSet<int>(lines)
            },
            null);

    /// <summary>Each model sits at its own line in the one file, so a line number identifies it.</summary>
    private static CheckReport Report(
        IEnumerable<(ClassifiedFinding Finding, int FileLine)> findings,
        ChangedLineResult? diff = null,
        IEnumerable<BaselineEntry>? fixedEntries = null,
        int gateFailures = 0)
    {
        var list = findings.ToList();
        var locations = new Dictionary<string, ClassLocation>(StringComparer.Ordinal);
        foreach (var (c, fileLine) in list)
            locations[c.Finding.ModelId] = new ClassLocation(LibraryFile, fileLine, LinesMapToFile: false);

        return new CheckReport(
            LibraryPath: Path.Combine(Repo, "Lib"),
            ModelsChecked: 10,
            Findings: list.Select(f => f.Finding).ToList(),
            Locations: locations,
            HasBaseline: true,
            GateFailureCount: gateFailures,
            FixedEntries: (fixedEntries ?? []).ToList(),
            Diff: diff);
    }

    private static JsonElement Review(CheckReport report) =>
        JsonDocument.Parse(new ReviewFindingFormatter().Format(report)).RootElement;

    private static JsonElement[] Comments(CheckReport report) =>
        Review(report).GetProperty("comments").EnumerateArray().ToArray();

    private static string Body(CheckReport report) => Review(report).GetProperty("body").GetString()!;

    // ---- what may be commented on -----------------------------------------------------------------

    [Fact]
    public void AFindingOnAChangedLineIsCommentedThere()
    {
        var comments = Comments(Report(
            [(new ClassifiedFinding(Finding(), FindingStatus.New), 7)],
            Diff(7)));

        var comment = Assert.Single(comments);
        Assert.Equal("Lib/package.mo", comment.GetProperty("path").GetString());   // repo-relative, forward slashes
        Assert.Equal(7, comment.GetProperty("line").GetInt32());
        Assert.Equal("RIGHT", comment.GetProperty("side").GetString());
        Assert.Contains("missing a description", comment.GetProperty("body").GetString());
    }

    [Fact]
    public void AFindingOnAnUntouchedLineIsNotCommented_ItGoesInTheSummary()
    {
        // The whole point: one comment outside the diff is a 422 that loses every other comment too.
        var report = Report(
            [(new ClassifiedFinding(Finding(model: "Lib.Old"), FindingStatus.New), 5)],
            Diff(7));

        Assert.Empty(Comments(report));
        Assert.Contains("Lib.Old", Body(report));
        Assert.Contains("not on a changed line", Body(report));
    }

    [Fact]
    public void AcceptedDebtIsNeverCommented_EvenOnAChangedLine()
    {
        // It is agreed history. Commenting on it asks the wrong person to fix it.
        var report = Report(
            [(new ClassifiedFinding(Finding(), FindingStatus.AcceptedDebt), 7)],
            Diff(7));

        Assert.Empty(Comments(report));
        Assert.DoesNotContain("Lib.M", Body(report));
    }

    [Fact]
    public void TouchedDebtIsCommented_AndSaysItIsPreExisting()
    {
        var comment = Assert.Single(Comments(Report(
            [(new ClassifiedFinding(Finding(), FindingStatus.TouchedDebt), 7)],
            Diff(7))));

        Assert.Contains("pre-existing", comment.GetProperty("body").GetString());
    }

    [Fact]
    public void WithNoDiffAtAll_NothingIsCommented()
    {
        // Defence in depth: the runner refuses this combination before the check even loads.
        var report = Report([(new ClassifiedFinding(Finding(), FindingStatus.New), 7)], diff: null);

        Assert.Empty(Comments(report));
    }

    [Fact]
    public void AFileOutsideTheRepositoryIsNotCommented()
    {
        var outside = new ChangedLineResult(
            true, @"C:\other-repo",
            new Dictionary<string, IReadOnlySet<int>> { [LibraryFile] = new HashSet<int> { 7 } },
            null);

        Assert.Empty(Comments(Report([(new ClassifiedFinding(Finding(), FindingStatus.New), 7)], outside)));
    }

    // ---- how they are grouped and capped ----------------------------------------------------------

    [Fact]
    public void TwoFindingsOnOneLineBecomeOneComment()
    {
        var report = Report(
            [
                (new ClassifiedFinding(Finding(model: "Lib.M", message: "no description"), FindingStatus.New), 7),
                (new ClassifiedFinding(Finding(model: "Lib.M2", message: "no icon"), FindingStatus.New), 7),
            ],
            Diff(7));

        var comment = Assert.Single(Comments(report));
        var body = comment.GetProperty("body").GetString()!;
        Assert.Contains("no description", body);
        Assert.Contains("no icon", body);
    }

    [Fact]
    public void BeyondTheCap_TheRestAreListedRatherThanDropped()
    {
        // 60 findings, each on its own changed line.
        var lines = Enumerable.Range(1, 60).ToArray();
        var report = Report(
            lines.Select(i => (new ClassifiedFinding(Finding(model: $"Lib.M{i}"), FindingStatus.New), i)),
            Diff(lines));

        Assert.Equal(50, Comments(report).Length);
        Assert.Contains("10 finding(s) not on a changed line", Body(report));
        Assert.Contains("Lib.M60", Body(report));
    }

    // ---- the summary ------------------------------------------------------------------------------

    [Fact]
    public void TheReviewIsAlwaysAComment_NeverARequestForChanges()
    {
        // The gate is the exit code. A tool that also blocks a human's merge button loses its token.
        var report = Report(
            [(new ClassifiedFinding(Finding(severity: RuleSeverity.Error), FindingStatus.New), 7)],
            Diff(7), gateFailures: 1);

        Assert.Equal("COMMENT", Review(report).GetProperty("event").GetString());
        Assert.Contains("gate: failed", Body(report));
    }

    [Fact]
    public void ACleanRunSaysSo()
    {
        var body = Body(Report([], Diff(7)));

        Assert.Contains("No new findings.", body);
        Assert.Contains("gate: passed", body);
    }

    [Fact]
    public void FixedFindingsAreCredited()
    {
        var body = Body(Report([], Diff(7),
            fixedEntries: [new BaselineEntry("fp", RuleIds.ClassIcon, "Lib.E", null, "gone")]));

        Assert.Contains("Fixed in changed models (1)", body);
        Assert.Contains("Lib.E", body);
    }

    [Fact]
    public void APipeInAMessageDoesNotBreakTheSummaryTable()
    {
        var body = Body(Report(
            [(new ClassifiedFinding(Finding(message: "a | b"), FindingStatus.New), 5)],
            Diff(7)));

        Assert.Contains(@"a \| b", body);
    }
}
