using System.Text.Json;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// A report that names a file has to name that file's line. Findings are produced with lines
/// relative to the class they are about, which for a class nested in a package.mo is a different
/// number entirely — and an annotation at the wrong line is worse than no annotation, because it
/// blames code that is fine.
/// </summary>
public class FileLineReportingTests
{
    // Line 1 is `within ;`, so `Late` starts at line 12 and its parameter is on line 13.
    private const string PackageWithLateClass = """
        within ;
        package Fix "A library"

          model First "Described"
            parameter Real a = 1 "a";
          end First;

          model Second "Described"
            parameter Real b = 2 "b";
          end Second;

          model Late
            parameter Real c = 3;
          end Late;

        end Fix;
        """;

    private const string Settings =
        """
        {
          "RuleSeverities": {
            "MLQT.Doc.ClassDescription": "Warning",
            "MLQT.Doc.ParameterDescription": "Warning",
            "MLQT.Doc.ClassDocumentationRevisions": "Warning"
          }
        }
        """;

    private sealed class TempLibrary : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mlqt-lines-" + Guid.NewGuid().ToString("N"));

        public TempLibrary(string packageSource, string order)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "Fix"));
            File.WriteAllText(System.IO.Path.Combine(Path, "Fix", "package.mo"), packageSource);
            File.WriteAllText(System.IO.Path.Combine(Path, "Fix", "package.order"), order);
            var settings = System.IO.Path.Combine(Path, ".mlqt");
            Directory.CreateDirectory(settings);
            File.WriteAllText(System.IO.Path.Combine(settings, "settings.json"), Settings);
        }

        public string LibraryPath => System.IO.Path.Combine(Path, "Fix");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static TempLibrary Fixture() =>
        new(PackageWithLateClass, "First\nSecond\nLate\n");

    private static List<JsonElement> FindingsFor(string libraryPath, string model)
    {
        var (_, stdout, _) = Run("check", libraryPath, "--format", "json", "--fail-on", "off");
        using var document = JsonDocument.Parse(stdout);
        return document.RootElement.GetProperty("findings")
            .EnumerateArray()
            .Where(f => f.GetProperty("Model").GetString() == model)
            .Select(f => f.Clone())
            .ToList();
    }

    [Fact]
    public void AClassLateInAPackageFile_IsReportedAtItsLineInThatFile()
    {
        using var lib = Fixture();

        var findings = FindingsFor(lib.LibraryPath, "Fix.Late");

        var missingDescription = findings.Single(f => f.GetProperty("RuleId").GetString() == "MLQT.Doc.ClassDescription");
        Assert.Equal(12, missingDescription.GetProperty("Line").GetInt32());   // `model Late` in package.mo
        Assert.Equal(1, missingDescription.GetProperty("ModelLine").GetInt32()); // line 1 of the class itself

        var undescribedParameter = findings.Single(f => f.GetProperty("RuleId").GetString() == "MLQT.Doc.ParameterDescription");
        Assert.Equal(13, undescribedParameter.GetProperty("Line").GetInt32());  // `parameter Real c = 3;`
    }

    [Fact]
    public void TheConsoleAndSarifAgreeWithTheJsonOnTheLine()
    {
        // One coordinate system across the formats: a reader following the console output and a
        // GitHub annotation must land in the same place.
        using var lib = Fixture();

        var (_, console, _) = Run("check", lib.LibraryPath, "--no-color", "--fail-on", "off");
        Assert.Contains("MLQT.Doc.ClassDescription (line 12)", console);

        var (_, sarif, _) = Run("check", lib.LibraryPath, "--format", "sarif", "--fail-on", "off");
        using var document = JsonDocument.Parse(sarif);
        var result = document.RootElement.GetProperty("runs")[0].GetProperty("results")
            .EnumerateArray()
            .Single(r => r.GetProperty("ruleId").GetString() == "MLQT.Doc.ClassDescription");
        var region = result.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal(12, region.GetProperty("region").GetProperty("startLine").GetInt32());
        Assert.Equal("package.mo", region.GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public void APackageWhoseSourceWasTrimmed_IsReportedAtItsDeclaration()
    {
        // A package's stored source has its inline children removed and the rest re-rendered, so a
        // line inside it is the renderer's, not the file's. Adding the offset anyway would point at a
        // real line belonging to another class; the package's own declaration is reported instead.
        using var lib = Fixture();

        var findings = FindingsFor(lib.LibraryPath, "Fix");

        var finding = Assert.Single(findings);
        // Inside the trimmed text this is not line 1, so the offset would have moved it — onto
        // `model First`, a class that is perfectly well documented.
        Assert.True(finding.GetProperty("ModelLine").GetInt32() > 1);
        Assert.Equal(2, finding.GetProperty("Line").GetInt32());   // `package Fix "A library"`
    }
}
