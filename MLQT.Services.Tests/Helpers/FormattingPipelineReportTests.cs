using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// What the user is told about the files Format All left alone for their syntax errors (B414).
/// </summary>
public class FormattingPipelineReportTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-report-repo");

    [Fact]
    public void EachFileIsNamedRelativeToTheRepository()
    {
        var message = FormattingPipelineReport.SkippedForSyntaxErrors(
            [Path.Combine(Root, "Lib", "Bad.mo"), Path.Combine(Root, "Lib", "Sub", "package.mo")], Root);

        Assert.StartsWith("2 file(s) were not formatted", message);
        Assert.Contains($": {Path.Combine("Lib", "Bad.mo")}, {Path.Combine("Lib", "Sub", "package.mo")}.", message);
        Assert.DoesNotContain(Root, message);
    }

    [Fact]
    public void WithNoRepository_EachFileIsNamedInFull()
    {
        var file = Path.Combine(Root, "Bad.mo");

        var message = FormattingPipelineReport.SkippedForSyntaxErrors([file], root: null);

        Assert.Contains($": {file}.", message);
    }

    [Fact]
    public void AFileOutsideTheRepository_IsNamedInFull()
    {
        var file = Path.Combine(Path.GetTempPath(), "elsewhere", "Bad.mo");

        var message = FormattingPipelineReport.SkippedForSyntaxErrors([file], Root);

        Assert.Contains($": {file}.", message);
    }

    [Fact]
    public void BeyondTheFirstFew_TheRestAreCounted()
    {
        var files = Enumerable.Range(1, FormattingPipelineReport.NamedFiles + 3)
            .Select(i => Path.Combine(Root, $"F{i}.mo"))
            .ToList();

        var message = FormattingPipelineReport.SkippedForSyntaxErrors(files, Root);

        Assert.StartsWith($"{files.Count} file(s)", message);
        Assert.Contains($"F{FormattingPipelineReport.NamedFiles}.mo and 3 more.", message);
        Assert.DoesNotContain($"F{FormattingPipelineReport.NamedFiles + 1}.mo", message);
    }
}
