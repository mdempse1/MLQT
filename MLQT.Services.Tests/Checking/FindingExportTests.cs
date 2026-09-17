using System.Text.Json;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// The desktop app's finding export (backlog B119).
/// </summary>
/// <remarks>
/// It was a hundred lines inside <c>CodeReview.razor.cs</c>, reachable only by rendering the page and
/// clicking Export — so the thing it promises, that its output can be diffed against the CLI's, was
/// asserted by nobody. It has already been broken once: B1 moved every report to the line in the
/// *file* and left this export on the class-relative line, after which the comparison script reported
/// nearly every finding as exclusive to both sides.
/// </remarks>
public class FindingExportTests
{
    private static LogMessage Finding(
        string model, string rule, int lineInClass, string? element = null, string details = "",
        RuleSeverity severity = RuleSeverity.Warning) =>
        // The display label and the stamped severity together, as Finding.ToLogMessage produces them:
        // the export writes the second and the app's list shows the first, so a fixture carrying only
        // one of them could not tell the two apart.
        new(model, $"Style {severity.ToString().ToLowerInvariant()}", lineInClass, $"{rule} on {model}", details)
        {
            RuleId = rule,
            ElementPath = element,
            Source = "StyleChecking",
            Fingerprint = $"{model}:{rule}:{lineInClass}",
            StyleSeverity = severity,
        };

    /// <summary>Class locations whose lines map to the file, which is the ordinary case.</summary>
    private static Dictionary<string, ClassLocation> Locations(params (string Model, string File, int StartLine)[] entries) =>
        entries.ToDictionary(
            e => e.Model,
            e => new ClassLocation(e.File, e.StartLine, LinesMapToFile: true),
            StringComparer.Ordinal);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void TheFieldNamesAreTheOnesTheCliWrites()
    {
        // The promise this export makes. A rename on either side makes the two files un-diffable, and
        // the symptom is a comparison reporting everything as exclusive rather than an error.
        // A fully populated finding: the optional fields are omitted when null (see below), so a
        // fixture without them would assert that half of this list is absent.
        var json = FindingExport.ToJson(
            [Finding("Lib.Model", "MLQT.Doc.ClassDescription", 3, element: "gain", details: "why")],
            Locations(("Lib.Model", Path.Combine("C:", "lib", "Model.mo"), 10)),
            new Dictionary<string, string> { ["Lib.Model"] = Path.Combine("C:", "lib") },
            "Project", new DateTime(2026, 9, 11, 8, 30, 0, DateTimeKind.Utc),
            statusOf: _ => "New");

        var finding = Parse(json).GetProperty("findings")[0];

        foreach (var field in new[]
                 {
                     "RuleId", "Severity", "Status", "Model", "Element", "Line", "ModelLine",
                     "Message", "Fingerprint", "File", "Source", "Details",
                 })
        {
            Assert.True(finding.TryGetProperty(field, out _), $"the export no longer writes {field}");
        }
    }

    [Theory]
    [InlineData(RuleSeverity.Error, "Error")]
    [InlineData(RuleSeverity.Warning, "Warning")]
    [InlineData(RuleSeverity.Info, "Info")]
    public void SeverityIsTheLevelAlone_AsTheCliWritesIt(RuleSeverity severity, string expected)
    {
        // The other half of the promise, and the half nothing was asserting: the names matched while
        // this meaning did not. The app's issue list shows "Style warning" so a style finding reads
        // differently from a parse diagnostic; the CLI's report writes "Warning", and this file exists
        // to be diffed against that one. Exported with the prefix, the severity column disagreed on
        // every row. `Source` is what separates the two kinds here.
        var json = FindingExport.ToJson(
            [Finding("Lib.Model", "MLQT.Doc.ClassDescription", 3, severity: severity)],
            Locations(("Lib.Model", Path.Combine("C:", "lib", "Model.mo"), 10)),
            new Dictionary<string, string>(),
            null, DateTime.UtcNow);

        Assert.Equal(expected, Parse(json).GetProperty("findings")[0].GetProperty("Severity").GetString());
    }

    [Fact]
    public void AParseDiagnosticsSeverityIsWrittenThrough()
    {
        // A parse diagnostic carries no configured severity — it is not a rule and never goes through
        // the severity map — so there is nothing structured to write and its own "Error" stands. The
        // CLI writes "Error" for it too, so the two still agree.
        var parseError = new LogMessage("Lib.Model", "Error", 1, "Parser error", "unexpected token")
        {
            Source = "Parser",
            RuleId = "MLQT.Parse.SyntaxError",
            Fingerprint = "fp",
        };

        var json = FindingExport.ToJson(
            [parseError],
            Locations(("Lib.Model", Path.Combine("C:", "lib", "Model.mo"), 10)),
            new Dictionary<string, string>(),
            null, DateTime.UtcNow);

        var finding = Parse(json).GetProperty("findings")[0];
        Assert.Equal("Error", finding.GetProperty("Severity").GetString());
        Assert.Equal("Parser", finding.GetProperty("Source").GetString());
    }

    [Fact]
    public void BothLinesAreWritten_AndTheyAreDifferentNumbers()
    {
        // The defect in full: Line is the line in the file, ModelLine the line within the class. A
        // report carrying only one of them cannot be compared with one carrying the other.
        var json = FindingExport.ToJson(
            [Finding("Lib.Model", "MLQT.Doc.ClassDescription", lineInClass: 3)],
            Locations(("Lib.Model", Path.Combine("C:", "lib", "Model.mo"), 10)),
            new Dictionary<string, string>(),
            null, DateTime.UtcNow);

        var finding = Parse(json).GetProperty("findings")[0];

        Assert.Equal(12, finding.GetProperty("Line").GetInt32());   // class starts at 10, +3, -1
        Assert.Equal(3, finding.GetProperty("ModelLine").GetInt32());
    }

    [Fact]
    public void TheFileIsRelativeToTheLibraryThatOwnsTheClass()
    {
        var json = FindingExport.ToJson(
            [Finding("Lib.Model", "R", 1)],
            Locations(("Lib.Model", Path.Combine("C:", "libs", "Lib", "Model.mo"), 1)),
            new Dictionary<string, string> { ["Lib.Model"] = Path.Combine("C:", "libs", "Lib") },
            null, DateTime.UtcNow);

        Assert.Equal("Model.mo", Parse(json).GetProperty("findings")[0].GetProperty("File").GetString());
    }

    [Fact]
    public void FindingsAreOrdered_SoTwoExportsOfTheSameThingAreTheSameFile()
    {
        // Ordered here rather than by the caller: the page holds them in the order they arrived from
        // a parallel check, which differs run to run, and a file that differs every time cannot be
        // diffed against anything - including yesterday's copy of itself.
        var json = FindingExport.ToJson(
            [
                Finding("Lib.Zeta", "R.Two", 5),
                Finding("Lib.Alpha", "R.Two", 9),
                Finding("Lib.Alpha", "R.Two", 2),
                Finding("Lib.Alpha", "R.One", 2),
            ],
            Locations(), new Dictionary<string, string>(), null, DateTime.UtcNow);

        var order = Parse(json).GetProperty("findings").EnumerateArray()
            .Select(f => $"{f.GetProperty("Model").GetString()}|{f.GetProperty("ModelLine").GetInt32()}|{f.GetProperty("RuleId").GetString()}")
            .ToList();

        Assert.Equal(
            ["Lib.Alpha|2|R.One", "Lib.Alpha|2|R.Two", "Lib.Alpha|9|R.Two", "Lib.Zeta|5|R.Two"],
            order);
    }

    [Fact]
    public void TheEnvelopeSaysWhatProducedItAndWhen()
    {
        var exportedAt = new DateTime(2026, 9, 11, 8, 30, 0, DateTimeKind.Utc);

        var root = Parse(FindingExport.ToJson(
            [Finding("Lib.Model", "R", 1), Finding("Lib.Other", "R", 1)],
            Locations(), new Dictionary<string, string>(), "My Project", exportedAt));

        Assert.Equal("mlqt-gui", root.GetProperty("tool").GetString());   // not the CLI's output
        Assert.Equal("My Project", root.GetProperty("project").GetString());
        Assert.Equal(2, root.GetProperty("findingCount").GetInt32());
        Assert.StartsWith("2026-09-11T08:30:00", root.GetProperty("exported").GetString());
    }

    [Fact]
    public void NullsAreOmittedRatherThanWrittenAsNull()
    {
        // Keeps the file readable, and matches the CLI's serializer options.
        var json = FindingExport.ToJson(
            [Finding("Lib.Model", "R", 1)],   // no element, no details
            Locations(), new Dictionary<string, string>(), null, DateTime.UtcNow);

        var finding = Parse(json).GetProperty("findings")[0];

        Assert.False(finding.TryGetProperty("Details", out _));
        Assert.False(finding.TryGetProperty("Status", out _));   // no baseline was supplied
    }

    [Fact]
    public void TheBaselineStatusIsWrittenWhenThereIsOne()
    {
        var json = FindingExport.ToJson(
            [Finding("Lib.Model", "R", 1)],
            Locations(), new Dictionary<string, string>(), null, DateTime.UtcNow,
            statusOf: _ => "AcceptedDebt");

        Assert.Equal("AcceptedDebt", Parse(json).GetProperty("findings")[0].GetProperty("Status").GetString());
    }

    [Fact]
    public void ALibraryLoadedFromAFileIsRootedAtItsDirectory()
    {
        // What the CLI does when it is pointed at a .mo rather than a package directory, so a finding
        // in a single-file library gets the same path from both.
        var roots = FindingExport.LibraryRootsByModel(
        [
            new LoadedLibrary
            {
                SourceType = LibrarySourceType.File,
                SourcePath = Path.Combine("C:", "libs", "Single.mo"),
                ModelIds = ["Single"],
            },
            new LoadedLibrary
            {
                SourceType = LibrarySourceType.Directory,
                SourcePath = Path.Combine("C:", "libs", "Package"),
                ModelIds = ["Package.A", "Package.B"],
            },
        ]);

        Assert.Equal(Path.Combine("C:", "libs"), roots["Single"]);
        Assert.Equal(Path.Combine("C:", "libs", "Package"), roots["Package.A"]);
        Assert.Equal(Path.Combine("C:", "libs", "Package"), roots["Package.B"]);
    }

    [Fact]
    public void ALibraryWithNoSourcePathContributesNothing()
    {
        // An encrypted library reconstructed from its documentation has no path on disk. Left in, it
        // would map every one of its classes to the empty string and send them all through the
        // relative-path rule against nothing.
        var roots = FindingExport.LibraryRootsByModel(
            [new LoadedLibrary { SourcePath = "", ModelIds = ["Ghost.Model"] }]);

        Assert.Empty(roots);
    }

    [Fact]
    public void TheFileNameCarriesTheMoment()
    {
        // Two exports in one session must not overwrite each other.
        Assert.Equal(
            "mlqt-findings-20260911-083000.json",
            FindingExport.FileNameFor(new DateTime(2026, 9, 11, 8, 30, 0)));
    }
}
