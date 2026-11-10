using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// Which class names a class's prose may use as words: its own library's, and those of the libraries
/// its library declares in <c>uses(...)</c> - never every class loaded.
/// </summary>
/// <remarks>
/// Every loaded class's name used to count, so a library loaded only for reference changed what was
/// flagged: with a tool's library folder loaded, MSL's <c>Kv</c>, <c>MPa</c> and <c>rgb</c> were
/// accepted because a vendor library had classes of those names, and the desktop app, the CLI and
/// the MCP server disagreed by 64 findings depending on which had the folder loaded.
/// </remarks>
public class KnownClassNamesTests
{
    private static DirectedGraph Graph()
    {
        var graph = new DirectedGraph();
        var mine = new ModelNode("MyLib", "MyLib", "package MyLib\nend MyLib;\n")
        {
            Uses = new Dictionary<string, string> { ["Dep"] = "1.0" }
        };
        graph.AddNode(mine);
        graph.AddNode(new ModelNode("MyLib.Widget", "Widget", "model Widget\nend Widget;\n"));
        graph.AddNode(new ModelNode("Dep", "Dep", "package Dep\nend Dep;\n"));
        graph.AddNode(new ModelNode("Dep.Zorblat", "Zorblat", "model Zorblat\nend Zorblat;\n"));
        graph.AddNode(new ModelNode("Vendor", "Vendor", "package Vendor\nend Vendor;\n"));
        graph.AddNode(new ModelNode("Vendor.Kv", "Kv", "model Kv\nend Kv;\n"));
        return graph;
    }

    [Fact]
    public void AClassMayUseTheNamesOfItsOwnLibrary_AndOfTheLibrariesItDeclares()
    {
        var names = KnownClassNames.For(Graph())("MyLib.Widget");

        Assert.Contains("Widget", names);
        Assert.Contains("MyLib", names);
        Assert.Contains("Zorblat", names);
        Assert.Contains("Dep", names);
    }

    [Fact]
    public void ALibraryThatIsLoadedButNotDeclared_LendsItsClassesNames_ToNobody()
    {
        var names = KnownClassNames.For(Graph())("MyLib.Widget");

        Assert.DoesNotContain("Kv", names);
        Assert.DoesNotContain("Vendor", names);
    }

    [Fact]
    public void ALibraryWithNoUsesAnnotation_HasOnlyItsOwnNames()
    {
        var names = KnownClassNames.For(Graph())("Dep.Zorblat");

        Assert.Equal(["Dep", "Zorblat"], names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheAnswerIsKeptPerLibrary_SoEveryClassOfOneLibraryShares_It()
    {
        var lookup = KnownClassNames.For(Graph());

        Assert.Same(lookup("MyLib.Widget"), lookup("MyLib"));
    }

    [Fact]
    public void TheContext_GivesEachClassItsOwnLibrarysNames_AndNothingWhenNothingIsSpelled()
    {
        var graph = Graph();
        var spelling = new StyleCheckingSettings { SpellCheckDescription = true, SpellCheckLanguages = ["en_US"] };

        var context = StyleCheckContext.Build(spelling, graph,
            new CustomDictionaryService(), new DictionaryManagerService());
        var none = StyleCheckContext.Build(new StyleCheckingSettings(), graph,
            new CustomDictionaryService(), new DictionaryManagerService());

        Assert.Contains("Zorblat", context.KnownModelNamesFor!("MyLib.Widget"));
        Assert.DoesNotContain("Zorblat", context.KnownModelNamesFor!("Vendor.Kv"));
        Assert.Null(none.KnownModelNamesFor);
    }

    [Fact]
    public void ADescriptionNamingAClassOfALibraryNotDeclared_IsReportedEvenWhileItIsLoaded()
    {
        // End to end through the shared check: the word that a loaded vendor library used to excuse.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("MyLib", "MyLib", "package MyLib \"Uses Kv and Zorblat\"\nend MyLib;\n")
        {
            Uses = new Dictionary<string, string> { ["Dep"] = "1.0" }
        });
        graph.AddNode(new ModelNode("Dep", "Dep", "package Dep\nend Dep;\n"));
        graph.AddNode(new ModelNode("Dep.Zorblat", "Zorblat", "model Zorblat\nend Zorblat;\n"));
        graph.AddNode(new ModelNode("Vendor", "Vendor", "package Vendor\nend Vendor;\n"));
        graph.AddNode(new ModelNode("Vendor.Kv", "Kv", "model Kv\nend Kv;\n"));
        var settings = new StyleCheckingSettings { SpellCheckDescription = true, SpellCheckLanguages = ["en_US"] };

        var findings = LibraryCheckSession.Check(graph, [graph.GetNode<ModelNode>("MyLib")!], settings,
            new CustomDictionaryService(), new DictionaryManagerService());

        Assert.Contains(findings, f => f.Message.Contains("'Kv'", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Message.Contains("'Zorblat'", StringComparison.Ordinal));
    }
}
