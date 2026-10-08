using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// What a tool says when it is given a class id that does not resolve. There are three different
/// reasons, and each has a different next step: nothing is loaded, the library the id starts with is
/// not loaded, or the library is loaded and has no such class. One message for all three sent an agent
/// searching a library that was never loaded.
/// </summary>
public class ClassNotFoundTests
{
    private const string Lib = """
        package Lib
          package Sub
            model Integrator
              Real x;
            equation
              der(x) = 1;
            end Integrator;
          end Sub;
        end Lib;
        """;

    private static ClassQueryTools Load(TestHost host)
    {
        host.Libraries.AddLibraryFromFileAsync(host.WriteMoFile("Lib.mo", Lib)).GetAwaiter().GetResult();
        return new ClassQueryTools(host.Libraries);
    }

    [Fact]
    public void ALibraryThatIsNotLoadedIsNamedWithHowToLoadIt()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("Modelica.Blocks.Continuous.LimPID")).Error;

        Assert.StartsWith("Library 'Modelica' is not loaded into MLQT, so 'Modelica.Blocks.Continuous.LimPID' cannot be found.", err);
        Assert.Contains("Loaded: Lib.", err);
        Assert.Contains("mlqt_load_library", err);
        Assert.Contains("simulator", err);
        Assert.Contains("getLoadedLibraries()", err);
        Assert.Contains("mlqt_search_classes", err);
    }

    [Fact]
    public void AClassOfTheSameNameLeadsAndLoadingIsTheAlternative()
    {
        // The library is not loaded, but a class of that name is: the likelier reading is that the agent
        // means that class, so it comes first, and loading a library is offered after it.
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("Modelica.Blocks.Continuous.Integrator")).Error;

        Assert.StartsWith(
            "No class 'Modelica.Blocks.Continuous.Integrator' is loaded into MLQT. " +
            "Classes named 'Integrator' that do exist: 'Lib.Sub.Integrator'.", err);
        Assert.Contains("If you meant a library 'Modelica', which is not loaded (loaded: Lib), load it with mlqt_load_library", err);
    }

    [Fact]
    public void AClassMissingFromALoadedLibraryNamesTheDeepestPackageThatExists()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("Lib.Sub.Deeper.Integratr")).Error;

        Assert.StartsWith("Library 'Lib' is loaded and has 'Lib.Sub', but no class 'Lib.Sub.Deeper.Integratr'", err);
        Assert.Contains("mlqt_get_package_tree with root_class_id 'Lib.Sub'", err);
        Assert.Contains("mlqt_search_classes", err);
        Assert.Contains("mlqt_search_text", err);
        Assert.Contains("mlqt_search_by_interface", err);
        // Not "load it": the library is loaded.
        Assert.DoesNotContain("mlqt_load_library", err);
    }

    [Fact]
    public void AClassMissingDirectlyUnderTheLibrarySaysOnlyThatTheLibraryLacksIt()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("Lib.Nope")).Error;

        Assert.StartsWith("Library 'Lib' is loaded, but has no class 'Lib.Nope'.", err);
        Assert.DoesNotContain("mlqt_get_package_tree", err);
    }

    [Fact]
    public void AnIdMissingItsLeadingPackagesIsPointedAtTheFullOne()
    {
        // 'Sub.Integrator' reads as a library called Sub; the class the agent meant is the useful part.
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("Sub.Integrator")).Error;

        Assert.StartsWith(
            "No class 'Sub.Integrator' is loaded into MLQT. Classes named 'Integrator' that do exist: 'Lib.Sub.Integrator'.", err);
    }

    [Fact]
    public void ABareClassNameIsGivenTheFullId()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("Integrator")).Error;

        Assert.StartsWith("No class 'Integrator' is loaded into MLQT. Classes named 'Integrator' that do exist: 'Lib.Sub.Integrator'.", err);
        Assert.Contains("fully-qualified", err);
    }

    [Fact]
    public void ABareNameMatchingNothingIsToldIdsAreFullNames()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("Nope")).Error;

        Assert.StartsWith("No class or library 'Nope' is loaded into MLQT.", err);
        Assert.Contains("fully-qualified", err);
        Assert.Contains("mlqt_search_classes", err);
    }

    [Fact]
    public void AnIdInTheWrongCaseIsGivenTheRightOne()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("lib.sub.integrator")).Error;

        // A typo, not a library to load: only the correction.
        Assert.Equal("No class 'lib.sub.integrator'. Did you mean 'Lib.Sub.Integrator'? Modelica names are case-sensitive.", err);
    }

    [Fact]
    public void AClassMissingFromALoadedLibraryAlsoNamesSameNamedClassesElsewhere()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(Load(host).GetClassInfo("Lib.Integrator")).Error;

        Assert.StartsWith(
            "Library 'Lib' is loaded, but has no class 'Lib.Integrator'. " +
            "Classes named 'Integrator' that do exist: 'Lib.Sub.Integrator'.", err);
    }

    [Fact]
    public void SameNamedClassesAreListedUpToALimit()
    {
        using var host = new TestHost();
        var many = "package Many\n" +
                   string.Concat(Enumerable.Range(1, ToolDiagnostics.MaxSuggestions + 2)
                       .Select(i => $"  package P{i}\n    model M\n    end M;\n  end P{i};\n")) +
                   "end Many;\n";
        host.Libraries.AddLibraryFromFileAsync(host.WriteMoFile("Many.mo", many)).GetAwaiter().GetResult();

        var err = ToolAssert.Error(new ClassQueryTools(host.Libraries).GetClassInfo("Other.M")).Error;

        Assert.Contains("'Many.P1.M'", err);
        Assert.DoesNotContain($"'Many.P{ToolDiagnostics.MaxSuggestions + 1}.M'", err);
        Assert.Contains("and 2 more.", err);
    }

    [Fact]
    public void ToolsTakingSeveralIdsListThemAllAndDiagnoseTheFirst()
    {
        using var host = new TestHost();
        Load(host);
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);

        var err = ToolAssert.Error(deps.AnalyzeImpact(["Lib.Sub.Integrator", "Modelica.Nope", "Lib.Nope"])).Error;

        Assert.StartsWith("2 class ids do not resolve: 'Modelica.Nope', 'Lib.Nope'. The first: Library 'Modelica' is not loaded", err);
    }

    [Fact]
    public void OneMissingIdOfSeveralGetsTheSingleDiagnosis()
    {
        using var host = new TestHost();
        Load(host);
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);

        var err = ToolAssert.Error(deps.AnalyzeImpact(["Lib.Nope"])).Error;

        Assert.StartsWith("Library 'Lib' is loaded, but has no class 'Lib.Nope'.", err);
    }
}
