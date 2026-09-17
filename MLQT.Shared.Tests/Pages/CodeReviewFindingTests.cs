using MLQT.Services.Checking;
using ModelicaParser.DataTypes;
using MLQT.Shared.Pages;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// The parts of the Code Review page that are decisions about a finding rather than about the UI:
/// which line the user is sent to, and whether a rule can be suppressed at all.
/// </summary>
public class CodeReviewFindingTests
{
    private static LogMessage Finding(string modelName, int line, string ruleId,
                                      string source = LogMessage.StyleCheckingSource) =>
        new(modelName, "Warning", line, "a finding") { RuleId = ruleId, Source = source };

    // ---- FileLineOf: class-relative line -> line in the file ----------------------------------

    [Fact]
    public void FileLineOf_AddsTheClassStartToTheFindingsLine()
    {
        // Findings carry a line relative to the class; the file line is what the user needs, and
        // ClassLocation is the single place that mapping is defined.
        var locations = new Dictionary<string, ClassLocation>
        {
            ["Lib.Thing"] = new(FilePath: "C:/lib/Thing.mo", StartLine: 100, LinesMapToFile: true),
        };

        Assert.Equal(104, CodeReview.FileLineOf(Finding("Lib.Thing", 5, RuleIds.OneOfEachSection), locations));
    }

    [Fact]
    public void FileLineOf_ForTheFirstLineOfAClass_IsTheClassStart()
    {
        var locations = new Dictionary<string, ClassLocation>
        {
            ["Lib.Thing"] = new("C:/lib/Thing.mo", StartLine: 100, LinesMapToFile: true),
        };

        Assert.Equal(100, CodeReview.FileLineOf(Finding("Lib.Thing", 1, RuleIds.OneOfEachSection), locations));
    }

    [Fact]
    public void FileLineOf_WhenTheStoredSourceIsNotTheFilesText_PointsAtTheClassDeclaration()
    {
        // A trimmed package or a re-rendered class: the offset would land on a real line that says
        // something else. Pointing at the right class is always true; pointing at the wrong line
        // looks precise and is not.
        var locations = new Dictionary<string, ClassLocation>
        {
            ["Lib.Pkg"] = new("C:/lib/package.mo", StartLine: 42, LinesMapToFile: false),
        };

        Assert.Equal(42, CodeReview.FileLineOf(Finding("Lib.Pkg", 17, RuleIds.OneOfEachSection), locations));
    }

    [Fact]
    public void FileLineOf_ForAClassWithNoKnownLocation_FallsBackToTheFindingsOwnLine()
    {
        var locations = new Dictionary<string, ClassLocation>();

        Assert.Equal(7, CodeReview.FileLineOf(Finding("Lib.Unknown", 7, RuleIds.OneOfEachSection), locations));
    }

    [Fact]
    public void FileLineOf_NeverReturnsALineBeforeTheFirst()
    {
        // A finding with no line at all must still open the file somewhere valid.
        var locations = new Dictionary<string, ClassLocation>();

        Assert.Equal(1, CodeReview.FileLineOf(Finding("Lib.Unknown", 0, RuleIds.OneOfEachSection), locations));
    }

    // ---- CanSuppressRule: which findings offer a __MLQT suppression ---------------------------

    [Fact]
    public void CanSuppressRule_ForAnOrdinaryStyleFinding_IsAllowed()
    {
        Assert.True(CodeReview.CanSuppressRule(Finding("Lib.Thing", 1, RuleIds.OneOfEachSection)));
    }

    [Fact]
    public void CanSuppressRule_ForAParseDiagnostic_IsRefused()
    {
        // Diagnostics are never configurable and never baselined, so offering to suppress one would
        // promise something the checker does not honour. RuleIds.IsDiagnostic is that question.
        Assert.True(RuleIds.IsDiagnostic(RuleIds.SyntaxError), "guard: the id under test is a diagnostic");

        Assert.False(CodeReview.CanSuppressRule(Finding("Lib.Thing", 1, RuleIds.SyntaxError)));
        Assert.False(CodeReview.CanSuppressRule(Finding("Lib.Thing", 1, RuleIds.ParseFailure)));
        Assert.False(CodeReview.CanSuppressRule(Finding("Lib.Thing", 1, RuleIds.CheckFailed)));
    }

    [Fact]
    public void CanSuppressRule_ForSomethingThatIsNotAStyleFinding_IsRefused()
    {
        // An external tool's message or a parser error has no rule to suppress.
        Assert.False(CodeReview.CanSuppressRule(
            Finding("Lib.Thing", 1, RuleIds.OneOfEachSection, LogMessage.ExternalToolSource)));
    }

    [Fact]
    public void CanSuppressRule_WithNoRuleId_IsRefused()
    {
        Assert.False(CodeReview.CanSuppressRule(
            new LogMessage("Lib.Thing", "Warning", 1, "a finding") { Source = LogMessage.StyleCheckingSource }));
    }

    [Fact]
    public void CanSuppressRule_WithNoFinding_IsRefused()
    {
        Assert.False(CodeReview.CanSuppressRule(null));
    }

    // ---- ReportPathOf: the file as an export shows it -----------------------------------------

    [Fact]
    public void ReportPathOf_IsRelativeToTheLibraryRoot_WithForwardSlashes()
    {
        // The GUI export's File field is meant to read the same as the CLI's. It used to write the
        // absolute path, which made it the second field the two exports disagreed on.
        var locations = new Dictionary<string, ClassLocation>
        {
            ["Lib.Thing"] = new(Path.Combine("C:", "libs", "Lib", "Sub", "Thing.mo"), 1, true),
        };
        var roots = new Dictionary<string, string> { ["Lib.Thing"] = Path.Combine("C:", "libs", "Lib") };

        Assert.Equal("Sub/Thing.mo", CodeReview.ReportPathOf(Finding("Lib.Thing", 1, RuleIds.OneOfEachSection), locations, roots));
    }

    [Fact]
    public void ReportPathOf_WithNoKnownLibraryRoot_KeepsTheFullPath()
    {
        var full = Path.GetFullPath(Path.Combine("C:", "libs", "Lib", "Thing.mo"));
        var locations = new Dictionary<string, ClassLocation> { ["Lib.Thing"] = new(full, 1, true) };

        var path = CodeReview.ReportPathOf(
            Finding("Lib.Thing", 1, RuleIds.OneOfEachSection), locations, new Dictionary<string, string>());

        Assert.Equal(full, path);
    }

    [Fact]
    public void ReportPathOf_WithNoKnownLocation_IsNull()
    {
        var path = CodeReview.ReportPathOf(
            Finding("Lib.Thing", 1, RuleIds.OneOfEachSection),
            new Dictionary<string, ClassLocation>(),
            new Dictionary<string, string>());

        Assert.Null(path);
    }
}
