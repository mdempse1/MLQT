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
        private readonly TempWorkspace _workspace = new("mlqt-lines");

        public TempLibrary(string packageSource, string order)
            => _workspace
                .Write(System.IO.Path.Combine("Fix", "package.mo"), packageSource)
                .Write(System.IO.Path.Combine("Fix", "package.order"), order)
                .WithSettings(Settings);

        public string Path => _workspace.Root;
        public string LibraryPath => _workspace.PathTo("Fix");

        public void Dispose() => _workspace.Dispose();
    }

    private static TempLibrary Fixture() =>
        new(PackageWithLateClass, "First\nSecond\nLate\n");

    private static List<JsonElement> FindingsFor(string libraryPath, string model)
    {
        var (_, stdout, _) = Cli.Run("check", libraryPath, "--format", "json", "--fail-on", "off");
        using var document = JsonDocument.Parse(stdout);
        return document.RootElement.GetProperty("findings")
            .EnumerateArray()
            .Where(f => f.GetProperty("Model").GetString() == model)
            .Select(f => f.Clone())
            .ToList();
    }

    [Fact]
    public void TheReportedPathIsTheSameHoweverTheLibraryWasNamed()
    {
        // A FileNode's path follows the library path the run was given, so the same class used to be
        // reported as "Fix/package.mo" or as an absolute path depending on how somebody typed the
        // command - and any consumer comparing paths had to know it.
        using var lib = Fixture();
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(lib.Path);

            var byRelative = FindingsFor("Fix", "Fix.Late").First().GetProperty("File").GetString();
            var byAbsolute = FindingsFor(lib.LibraryPath, "Fix.Late").First().GetProperty("File").GetString();

            Assert.Equal(byRelative, byAbsolute);
            Assert.Equal("package.mo", byRelative);   // relative to the library, forward slashes
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
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

        var (_, console, _) = Cli.Run("check", lib.LibraryPath, "--no-color", "--fail-on", "off");
        Assert.Contains("MLQT.Doc.ClassDescription (line 12)", console);

        var (_, sarif, _) = Cli.Run("check", lib.LibraryPath, "--format", "sarif", "--fail-on", "off");
        using var document = JsonDocument.Parse(sarif);
        var result = document.RootElement.GetProperty("runs")[0].GetProperty("results")
            .EnumerateArray()
            .Single(r => r.GetProperty("ruleId").GetString() == "MLQT.Doc.ClassDescription");
        var region = result.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal(12, region.GetProperty("region").GetProperty("startLine").GetInt32());
        Assert.Equal("package.mo", region.GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public void APackageWhoseSourceWasTrimmed_IsReportedAtTheLineItIsReallyAbout()
    {
        // A package's stored source has its inline children removed. That used to mean re-rendering
        // the rest, so a line inside it was the renderer's and not the file's, and the report fell
        // back to the package's own declaration — true, but no more precise than naming the class.
        //
        // B216 excises the children's lines instead, so what is left is the file's own text with
        // runs of lines missing, and the node carries the map. The offset is taken after putting
        // those lines back, which lands on the line the finding is actually about: line 16 is
        // `end Fix;`, where the missing Documentation(revisions=…) annotation belongs.
        using var lib = Fixture();

        var findings = FindingsFor(lib.LibraryPath, "Fix");

        var finding = Assert.Single(findings);

        // Inside the trimmed text this is not line 1, so a report that ignored the map would be
        // somewhere else entirely — and one that ignored the trim would land on `model First`, a
        // class that is perfectly well documented.
        Assert.True(finding.GetProperty("ModelLine").GetInt32() > 1);
        Assert.Equal(16, finding.GetProperty("Line").GetInt32());
    }

    [Fact]
    public void TheTrimmedLineMapDoesNotDriftOntoAnotherClass()
    {
        // The failure this guards against is the one that made the old fallback the safe answer: an
        // offset applied to trimmed text points into a class that is not the one being reported on.
        // `model First` is fully documented, so a finding landing on its lines would be a finding
        // about code with nothing wrong with it.
        using var lib = Fixture();

        var finding = Assert.Single(FindingsFor(lib.LibraryPath, "Fix"));
        var line = finding.GetProperty("Line").GetInt32();

        Assert.False(line is >= 4 and <= 6, $"line {line} is inside `model First`");
        Assert.False(line is >= 8 and <= 10, $"line {line} is inside `model Second`");
        Assert.False(line is >= 12 and <= 14, $"line {line} is inside `model Late`");
    }
}
