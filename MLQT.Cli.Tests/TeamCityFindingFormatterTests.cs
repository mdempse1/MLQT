using MLQT.Services.Checking;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// TeamCity service messages: the statistics that draw the debt burndown, and the build problem that
/// marks the build failed.
///
/// <para>The escaping is the part that had no test at all, though the phase-4 note asked for one by
/// name. It matters more than it looks: TeamCity parses these lines out of stdout, so a character it
/// treats as syntax inside a message is not a cosmetic problem — an unescaped <c>'</c> ends the
/// attribute early and the rest of the finding is read as service-message syntax, which either
/// mangles the report or makes TeamCity ignore the line. Modelica messages carry all of the offending
/// characters: <c>'</c> in a quoted identifier, <c>[</c> and <c>]</c> in an array dimension, and the
/// pipe in a message quoting an equation.</para>
/// </summary>
public class TeamCityFindingFormatterTests
{
    private static Finding Finding(
        string message = "The class is missing a description string",
        RuleSeverity severity = RuleSeverity.Warning,
        string model = "Lib.M") =>
        new()
        {
            RuleId = RuleIds.ClassDescription,
            ModelId = model,
            Message = message,
            Severity = severity,
            LineNumber = 3,
        };

    private static CheckReport Report(
        IEnumerable<ClassifiedFinding>? findings = null,
        IEnumerable<BaselineEntry>? fixedEntries = null,
        int gateFailures = 0) =>
        new(
            LibraryPath: @"C:\lib",
            ModelsChecked: 10,
            Findings: (findings ?? []).ToList(),
            Locations: new Dictionary<string, ClassLocation>(),
            HasBaseline: true,
            GateFailureCount: gateFailures,
            FixedEntries: (fixedEntries ?? []).ToList());

    private static string Format(CheckReport report) => new TeamCityFindingFormatter().Format(report);

    [Fact]
    public void TheStatisticsCountEachStatus()
    {
        var text = Format(Report(
            [
                new ClassifiedFinding(Finding(model: "Lib.A"), FindingStatus.New),
                new ClassifiedFinding(Finding(model: "Lib.B"), FindingStatus.New),
                new ClassifiedFinding(Finding(model: "Lib.C"), FindingStatus.TouchedDebt),
                new ClassifiedFinding(Finding(model: "Lib.D"), FindingStatus.AcceptedDebt),
            ],
            fixedEntries: [new BaselineEntry("fp", RuleIds.ClassIcon, "Lib.E", null, "gone")]));

        Assert.Contains("##teamcity[buildStatisticValue key='mlqt.findings.new' value='2']", text);
        Assert.Contains("##teamcity[buildStatisticValue key='mlqt.findings.acceptedDebt' value='1']", text);
        Assert.Contains("##teamcity[buildStatisticValue key='mlqt.findings.touchedDebt' value='1']", text);
        Assert.Contains("##teamcity[buildStatisticValue key='mlqt.findings.fixed' value='1']", text);
    }

    [Fact]
    public void AcceptedDebtIsNotReportedPerFinding()
    {
        // It is in the statistics, which is what draws the burndown, but a per-finding message for
        // debt everybody has already agreed to live with is thousands of lines of build log.
        var text = Format(Report([
            new ClassifiedFinding(Finding(message: "new one"), FindingStatus.New),
            new ClassifiedFinding(Finding(message: "old one"), FindingStatus.AcceptedDebt),
        ]));

        Assert.Contains("new one", text);
        Assert.DoesNotContain("old one", text);
    }

    [Fact]
    public void AnErrorIsReportedAsOne()
    {
        var text = Format(Report([
            new ClassifiedFinding(Finding(severity: RuleSeverity.Error), FindingStatus.New),
        ]));

        Assert.Contains("status='ERROR'", text);
    }

    [Fact]
    public void BuildProblemIsEmittedOnlyWhenTheGateFailed()
    {
        var failed = Format(Report(
            [new ClassifiedFinding(Finding(), FindingStatus.New)], gateFailures: 1));
        var passed = Format(Report(
            [new ClassifiedFinding(Finding(), FindingStatus.New)]));

        Assert.Contains("##teamcity[buildProblem", failed);
        Assert.DoesNotContain("##teamcity[buildProblem", passed);
    }

    [Theory]
    // The vertical bar is TeamCity's own escape character, so it has to go first or every other
    // escape below gets escaped again by the next replacement.
    [InlineData("a|b", "a||b")]
    [InlineData("don't", "don|'t")]
    [InlineData("x[1]", "x|[1|]")]
    [InlineData("one\ntwo", "one|ntwo")]
    [InlineData("one\r\ntwo", "one|r|ntwo")]
    public void MessageTextIsEscaped(string message, string expected)
    {
        var text = Format(Report([new ClassifiedFinding(Finding(message: message), FindingStatus.New)]));

        Assert.Contains(expected, text);
    }

    /// <summary>
    /// The order of the replacements, tested from the outside, on the one input that can tell them
    /// apart: a bar immediately followed by a quote. Escaping the bar first gives <c>||</c> then
    /// <c>|'</c>, so <c>|||'</c>. Escaping the quote first writes a bar of its own, which the bar
    /// pass then doubles — <c>||||'</c>, one character adrift, and every message after it in the
    /// build log is read as service-message syntax.
    /// </summary>
    [Fact]
    public void EscapingIsNotAppliedToItsOwnOutput()
    {
        var text = Format(Report([
            new ClassifiedFinding(Finding(message: "bar-then-quote |' here"), FindingStatus.New),
        ]));

        Assert.Contains("bar-then-quote |||' here", text);
        Assert.DoesNotContain("||||", text);
    }

    [Fact]
    public void TheBuildProblemDescriptionIsEscapedToo()
    {
        // It is built from a count and fixed wording today, but it goes through the same escaper, and
        // an unescaped build problem is the line that decides whether the build is marked failed.
        var text = Format(Report(
            [new ClassifiedFinding(Finding(), FindingStatus.New)], gateFailures: 3));

        var problem = text.Split('\n').Single(l => l.StartsWith("##teamcity[buildProblem", StringComparison.Ordinal));
        Assert.EndsWith("']", problem.TrimEnd('\r'));
        Assert.Contains("3 finding", problem);
    }

    [Fact]
    public void NoFindingsStillReportsTheStatistics()
    {
        // A clean build has to contribute a zero, or the trend line simply skips the commit and the
        // burndown reads as though nothing was checked.
        var text = Format(Report());

        Assert.Contains("mlqt.findings.new' value='0'", text);
        Assert.DoesNotContain("##teamcity[buildProblem", text);
    }
}
