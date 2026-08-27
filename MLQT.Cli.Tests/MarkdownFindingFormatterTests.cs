using MLQT.Cli;
using MLQT.Services.Checking;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// The markdown summary a CI job pastes into a pull request. It is read by people deciding whether to
/// merge, so what it leaves out matters as much as what it says: accepted debt is not news, a fixed
/// finding is worth crediting, and a message containing a pipe must not silently break the table it
/// is written into.
/// </summary>
public class MarkdownFindingFormatterTests
{
    private static Finding Finding(
        string ruleId = RuleIds.ClassDescription,
        string model = "Lib.M",
        string message = "The class is missing a description string",
        RuleSeverity severity = RuleSeverity.Warning,
        int line = 3) =>
        new()
        {
            RuleId = ruleId,
            ModelId = model,
            Message = message,
            Severity = severity,
            LineNumber = line,
        };

    private static CheckReport Report(
        IEnumerable<ClassifiedFinding>? findings = null,
        IEnumerable<BaselineEntry>? fixedEntries = null,
        int gateFailures = 0) =>
        new(
            LibraryPath: @"C:\lib",
            ModelsChecked: 10,
            Findings: (findings ?? []).ToList(),
            ModelToFile: new Dictionary<string, string>(),
            HasBaseline: true,
            GateFailureCount: gateFailures,
            FixedEntries: (fixedEntries ?? []).ToList());

    private static string Format(CheckReport report) => new MarkdownFindingFormatter().Format(report);

    [Fact]
    public void TheHeadingCountsEachStatusAndSaysWhetherTheGatePassed()
    {
        var text = Format(Report(
            [
                new ClassifiedFinding(Finding(model: "Lib.A"), FindingStatus.New),
                new ClassifiedFinding(Finding(model: "Lib.B"), FindingStatus.TouchedDebt),
                new ClassifiedFinding(Finding(model: "Lib.C"), FindingStatus.AcceptedDebt),
                new ClassifiedFinding(Finding(model: "Lib.D"), FindingStatus.AcceptedDebt),
            ],
            fixedEntries: [new BaselineEntry("fp", RuleIds.ClassIcon, "Lib.E", null, "gone")],
            gateFailures: 1));

        Assert.Contains("1 new", text);
        Assert.Contains("1 touched", text);
        Assert.Contains("2 accepted", text);
        Assert.Contains("1 fixed", text);
        Assert.Contains("gate: failed", text);
    }

    [Fact]
    public void APassingGate_SaysSo()
    {
        Assert.Contains("gate: passed", Format(Report()));
    }

    [Fact]
    public void NothingActionable_SaysSoInsteadOfShowingAnEmptyTable()
    {
        var text = Format(Report([new ClassifiedFinding(Finding(), FindingStatus.AcceptedDebt)]));

        Assert.Contains("No new findings.", text);
        Assert.DoesNotContain("| Severity |", text);
    }

    [Fact]
    public void AcceptedDebt_IsCountedButNotListed()
    {
        // It is the state of the world the team already agreed to; listing it in a pull request buries
        // the finding the pull request introduced.
        var text = Format(Report(
        [
            new ClassifiedFinding(Finding(model: "Lib.New"), FindingStatus.New),
            new ClassifiedFinding(Finding(model: "Lib.Old"), FindingStatus.AcceptedDebt),
        ]));

        Assert.Contains("Lib.New", text);
        Assert.DoesNotContain("Lib.Old", text);
    }

    [Fact]
    public void TouchedDebt_IsListed()
    {
        // Pre-existing, but in a model this change touched — which is the whole point of reporting it.
        var text = Format(Report([new ClassifiedFinding(Finding(model: "Lib.Touched"), FindingStatus.TouchedDebt)]));

        Assert.Contains("Lib.Touched", text);
        Assert.Contains("TouchedDebt", text);
    }

    [Fact]
    public void AFindingRowCarriesWhatIsNeededToActOnIt()
    {
        var text = Format(Report(
            [new ClassifiedFinding(
                Finding(ruleId: RuleIds.MissingUnit, model: "Lib.M", message: "no unit on x",
                        severity: RuleSeverity.Error, line: 42),
                FindingStatus.New)]));

        Assert.Contains("| error | New | MLQT.Units.MissingUnit | Lib.M | 42 | no unit on x |", text);
    }

    [Fact]
    public void FixedFindings_AreListedAndOrdered()
    {
        var text = Format(Report(
            fixedEntries:
            [
                new BaselineEntry("f2", RuleIds.ClassIcon, "Lib.Z", null, "second"),
                new BaselineEntry("f1", RuleIds.ClassDescription, "Lib.A", null, "first"),
            ]));

        Assert.Contains("**Fixed in changed models (2):**", text);
        Assert.True(text.IndexOf("Lib.A", StringComparison.Ordinal)
                    < text.IndexOf("Lib.Z", StringComparison.Ordinal),
            "fixed entries should be ordered by model so the list is stable between runs");
    }

    [Fact]
    public void NothingFixed_AddsNoSection()
    {
        Assert.DoesNotContain("Fixed in changed models", Format(Report()));
    }

    [Fact]
    public void APipeInAMessage_DoesNotBreakTheTable()
    {
        // A Modelica description can contain anything; an unescaped pipe would silently split the row
        // into extra columns and lose the rest of the message.
        var text = Format(Report(
            [new ClassifiedFinding(Finding(message: "a | b"), FindingStatus.New)]));

        Assert.Contains(@"a \| b", text);
    }

    [Fact]
    public void ANewlineInAMessage_IsFlattenedIntoTheRow()
    {
        var text = Format(Report(
            [new ClassifiedFinding(Finding(message: "first\r\nsecond"), FindingStatus.New)]));

        var row = text.Split('\n').Single(l => l.Contains("first", StringComparison.Ordinal));
        Assert.Contains("first  second", row);
    }

    [Fact]
    public void APipeInAFixedEntry_IsEscapedToo()
    {
        var text = Format(Report(
            fixedEntries: [new BaselineEntry("fp", RuleIds.ClassDescription, "Lib.A|B", null, "x | y")]));

        Assert.Contains(@"Lib.A\|B", text);
        Assert.Contains(@"x \| y", text);
    }
}
