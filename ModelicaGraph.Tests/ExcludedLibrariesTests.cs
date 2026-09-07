using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Excluding a library stops it being reported on, without taking it out of the graph — a test-case
/// library sitting in the same repository as the code it exercises must still count as a user of that
/// code, or excluding it would make the code look unused.
/// </summary>
public class ExcludedLibrariesTests
{
    private static StyleCheckingSettings Excluding(params string[] names)
    {
        var settings = new StyleCheckingSettings { ClassHasDescription = true };
        settings.ExcludedLibraries.AddRange(names);
        return settings;
    }

    // --- name matching --------------------------------------------------------------------------

    [Theory]
    [InlineData("Tests", "Tests.SomeCase", true)]
    [InlineData("Tests", "Tests", true)]                    // the library root itself
    [InlineData("Tests", "TestsHelper.Thing", false)]       // prefix, not the same library
    [InlineData("Tests", "Lib.Tests.Thing", false)]         // only the FIRST segment is the library
    [InlineData("tests", "Tests.SomeCase", true)]           // case-insensitive
    [InlineData("*_Tests", "Foo_Tests.Case", true)]
    [InlineData("*_Tests", "Bar_Tests.Case", true)]
    [InlineData("*_Tests", "Foo.Case", false)]
    [InlineData("Foo*", "FooBar.Case", true)]
    public void LibraryNameMatching(string pattern, string modelId, bool excluded)
        => Assert.Equal(excluded, Excluding(pattern).IsLibraryExcluded(modelId));

    [Fact]
    public void NoExclusions_ExcludesNothing()
    {
        var settings = new StyleCheckingSettings();
        Assert.False(settings.IsLibraryExcluded("Anything.At.All"));
    }

    [Fact]
    public void EditingTheListTakesEffect()
    {
        // The settings object is mutated in place by the UI, so the compiled patterns must notice.
        var settings = new StyleCheckingSettings();
        Assert.False(settings.IsLibraryExcluded("Tests.Case"));

        settings.ExcludedLibraries.Add("Tests");
        Assert.True(settings.IsLibraryExcluded("Tests.Case"));

        settings.ExcludedLibraries.Clear();
        Assert.False(settings.IsLibraryExcluded("Tests.Case"));
    }

    // --- effect on checking ---------------------------------------------------------------------

    [Fact]
    public void PerClassRulesAreNotReportedForAnExcludedLibrary()
    {
        var model = new ModelDefinition("Case", "model Case\n  Real x;\nend Case;");

        var reported = StyleChecking.RunStyleCheckingFindings(model, Excluding("Other"), "Tests.Case");
        Assert.NotEmpty(reported);   // no description → a finding, when not excluded

        var suppressed = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("Case", "model Case\n  Real x;\nend Case;"), Excluding("Tests"), "Tests.Case");
        Assert.Empty(suppressed);
    }

    [Fact]
    public void GraphAnalysesSkipExcludedLibraries_ButStillCountTheirReferences()
    {
        // Lib.Used is referenced only from the excluded test library. Excluding the tests must silence
        // findings ABOUT the tests without resurrecting "Lib.Used is unused".
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib end Lib;") { ClassType = "package" });
        graph.AddNode(new ModelNode("Lib.Used", "Used", "model Used end Used;")
        { ClassType = "model", IsNested = true, ParentModelName = "Lib" });
        graph.AddNode(new ModelNode("Tests", "Tests", "package Tests end Tests;") { ClassType = "package" });
        graph.AddNode(new ModelNode("Tests.Case", "Case", "model Case end Case;")
        { ClassType = "model", IsNested = true, ParentModelName = "Tests" });
        graph.AddModelUsesModel("Tests.Case", "Lib.Used");
        graph.MarkDependenciesAnalyzed();

        var settings = Excluding("Tests");
        settings.CheckUnusedPublicClass = true;
        var context = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());

        var findings = GraphAnalysisRunner.Run(context, new IGraphAnalyzer[] { new UnusedClassAnalyzer() });

        Assert.DoesNotContain(findings, f => f.ModelId.StartsWith("Tests"));
        Assert.DoesNotContain(findings, f => f.ModelId == "Lib.Used");
    }

    [Fact]
    public void WithoutTheExclusion_TheTestLibraryIsReported()
    {
        // Guards the premise of the test above.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Tests", "Tests", "package Tests end Tests;") { ClassType = "package" });
        graph.AddNode(new ModelNode("Tests.Case", "Case", "model Case end Case;")
        { ClassType = "model", IsNested = true, ParentModelName = "Tests" });
        graph.MarkDependenciesAnalyzed();

        var settings = new StyleCheckingSettings { CheckUnusedPublicClass = true };
        var context = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());

        Assert.Contains(
            GraphAnalysisRunner.Run(context, new IGraphAnalyzer[] { new UnusedClassAnalyzer() }),
            f => f.ModelId == "Tests.Case");
    }
}
