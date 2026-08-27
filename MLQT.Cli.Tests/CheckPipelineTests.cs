using MLQT.Cli;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// What `mlqt check` says on stderr about the run it just did.
///
/// <para>The notes and warnings are the only account a CI job gives of what was actually checked. A
/// run that quietly skipped a library, resolved references against nothing, or spell-checked against
/// a dictionary the machine does not have still exits zero and still looks like a pass — so each of
/// those has to be said out loud, and the exit code has to distinguish a setup mistake (2) from a
/// gate failure (1).</para>
/// </summary>
public class CheckPipelineTests : IDisposable
{
    private readonly List<string> _roots = [];

    public void Dispose()
    {
        foreach (var root in _roots)
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private string NewDirectory(string name = "lib")
    {
        var path = Path.Combine(Path.GetTempPath(), "mlqt-pipeline", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        _roots.Add(Directory.GetParent(path)!.FullName);
        return path;
    }

    private static void Write(string directory, string relativePath, string content)
    {
        var full = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static string Library(string directory, string name = "TestLib")
    {
        Write(directory, "package.mo",
            $"package {name} \"A test library\"\n  model M \"A model\"\n    Real x;\n  end M;\nend {name};");
        return directory;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
    }

    // ── nothing to check ──

    [Fact]
    public void ADirectoryWithNoLibraryInIt_IsASetupErrorNotAPass()
    {
        // Pointing the job at the wrong directory would otherwise check nothing, find nothing, and
        // report a green build for a library that was never looked at.
        var empty = NewDirectory();
        File.WriteAllText(Path.Combine(empty, "README.md"), "no Modelica here");

        var (code, _, stderr) = Run("check", empty);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("no Modelica libraries found", stderr);
    }

    [Fact]
    public void ASettingsFileThatCannotBeRead_IsASetupError()
    {
        var lib = Library(NewDirectory());
        Write(lib, ".mlqt/settings.json", "{ this is not json");

        var (code, _, stderr) = Run("check", lib);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("error:", stderr);
    }

    [Fact]
    public void ARunWithNoRulesEnabled_SaysSoRatherThanReportingACleanLibrary()
    {
        var lib = Library(NewDirectory());
        Write(lib, ".mlqt/settings.json", "{ }");

        var (code, _, stderr) = Run("check", lib);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("no style rules are enabled", stderr);
    }

    [Fact]
    public void AnExcludedLibrary_IsNamedAndItsClassesCountedOut()
    {
        // A mistyped exclusion is invisible otherwise: the run passes and nobody knows the library
        // it was meant to skip was checked, or that the one under test was not.
        var lib = Library(NewDirectory());
        Write(lib, ".mlqt/settings.json",
            """{ "ClassHasDescription": true, "ExcludedLibraries": ["TestLib"] }""");

        var (_, _, stderr) = Run("check", lib);

        Assert.Contains("excluding TestLib", stderr);
        Assert.Contains("skipped as excluded", stderr);
    }

    // ── dependencies ──

    [Fact]
    public void ADependencyPathThatIsNotThere_StopsTheRun()
    {
        // Carrying on would resolve every reference into that dependency to nothing and report a
        // pile of broken references that are not broken.
        var lib = Library(NewDirectory());
        var missing = Path.Combine(Path.GetTempPath(), $"mlqt-absent-{Guid.NewGuid():N}");

        var (code, _, stderr) = Run("check", lib, "--dependency", missing);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("dependency path not found", stderr);
    }

    [Fact]
    public void TheLibrariesLoadedForReference_AreNamedOnce()
    {
        var lib = Library(NewDirectory());
        var dependency = Library(NewDirectory("DepLib"), "DepLib");

        var (code, _, stderr) = Run("check", lib, "--dependency", dependency);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("loaded DepLib for reference resolution", stderr);
    }

    // ── encrypted libraries, which can only be loaded for reference ──

    private string EncryptedLibrary(string name, string version, bool withHelp)
    {
        var root = NewDirectory($"{name} {version}");
        File.WriteAllText(Path.Combine(root, "package.moe"), "encrypted");
        if (withHelp)
            Write(root, $"help/{name}.html",
                "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>" +
                $"<h2><a name=\"{name}\"></a>{name}</h2>" +
                "<p><span class=\"ModelicaDescription\">A commercial library</span></p>" +
                "</body></html>");
        return root;
    }

    [Fact]
    public void AnEncryptedDependencyThatShipsNoDocumentation_IsWarnedAbout()
    {
        // Nothing can be recovered from it, so every reference into it stays unresolved. Silence
        // here sends people looking for a fault in their own code.
        var lib = Library(NewDirectory());
        var encrypted = EncryptedLibrary("Sealed", "1.0", withHelp: false);

        var (code, _, stderr) = Run("check", lib, "--dependency", encrypted);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("encrypted library 'Sealed' ships no usable documentation", stderr);
    }

    [Fact]
    public void AnEncryptedDependencyWithDocumentation_IsLoadedQuietly()
    {
        // Its classes were recovered, so there is nothing actionable to say — and with a tool's whole
        // library folder on --dependency, a note per library is fifty lines of noise.
        var lib = Library(NewDirectory());
        var encrypted = EncryptedLibrary("Commercial", "2.1", withHelp: true);

        var (code, _, stderr) = Run("check", lib, "--dependency", encrypted);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.DoesNotContain("ships no usable documentation", stderr);
        Assert.Contains("Commercial", stderr);
    }

    [Fact]
    public void AnEncryptedLibraryInsideTheRepository_IsNotReportedOn()
    {
        // There is no source in it to have an opinion about — only classes rebuilt from the vendor's
        // documentation. Reporting on it would be reporting on our own reconstruction.
        var root = NewDirectory("Repo");
        Library(Path.Combine(root, "TestLib"));
        Directory.CreateDirectory(Path.Combine(root, "Sealed 1.0"));
        File.WriteAllText(Path.Combine(root, "Sealed 1.0", "package.moe"), "encrypted");
        Write(root, ".mlqt/settings.json", """{ "ClassHasDescription": true }""");

        var (code, stdout, stderr) = Run("check", root, "--format", "json");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("encrypted library 'Sealed' ships no usable documentation", stderr);
        Assert.DoesNotContain("Sealed", stdout);
    }

    // ── spell checking against a dictionary that is not installed ──

    [Fact]
    public void ASpellCheckLanguageWithNoDictionary_IsWarnedAbout()
    {
        // Hunspell would otherwise fall back silently and check the prose against another language,
        // which produces findings that look real and are not.
        var lib = Library(NewDirectory());
        Write(lib, ".mlqt/settings.json",
            """{ "SpellCheckDescription": true, "SpellCheckLanguages": ["zz_ZZ"] }""");

        var (code, _, stderr) = Run("check", lib);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("warning:", stderr);
        Assert.Contains("zz_ZZ", stderr);
    }

    [Fact]
    public void NoSpellCheckingMeansNoDictionaryWarning()
    {
        var lib = Library(NewDirectory());
        Write(lib, ".mlqt/settings.json",
            """{ "ClassHasDescription": true, "SpellCheckLanguages": ["zz_ZZ"] }""");

        var (_, _, stderr) = Run("check", lib);

        Assert.DoesNotContain("zz_ZZ", stderr);
    }

    // ── work the run only pays for when a rule asks for it ──

    [Fact]
    public void ARuleThatNeedsTheDependencyEdges_MakesTheRunBuildThem()
    {
        // The edges are not populated by loading; a rule that reads them would otherwise be answered
        // from an empty graph and report every class as unused.
        var lib = Library(NewDirectory());
        Write(lib, ".mlqt/settings.json",
            """{ "RuleSeverities": { "MLQT.Unused.Class": "Warning" } }""");

        var (code, _, stderr) = Run("check", lib);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("running dependency analysis", stderr);
    }

    [Fact]
    public void ARunWithNoSuchRule_DoesNotPayForTheDependencyPass()
    {
        // It is the expensive half of a check. On a repository with a tool's library folder loaded
        // for reference it is minutes, for an output that cannot change.
        var lib = Library(NewDirectory());
        Write(lib, ".mlqt/settings.json", """{ "ClassHasDescription": true }""");

        var (_, _, stderr) = Run("check", lib);

        Assert.DoesNotContain("running dependency analysis", stderr);
    }

    [Fact]
    public void AClassDeclaredByTwoLibrariesInTheSameRepository_IsCheckedOnce()
    {
        // Two copies of a library under one root (a vendored fork beside the original) resolve to
        // the same class id. Reporting both would double every finding in it.
        var root = NewDirectory("Repo");
        Library(Path.Combine(root, "first"));
        Library(Path.Combine(root, "second"));
        Write(root, ".mlqt/settings.json", """{ "ParameterHasDescription": true }""");

        var (code, stdout, _) = Run("check", root, "--format", "json");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("\"modelsChecked\": 2", stdout);
    }
}
