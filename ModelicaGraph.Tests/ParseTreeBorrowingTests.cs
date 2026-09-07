using System;
using System.Collections.Concurrent;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Who gives a parse tree back.
///
/// <para>A tree is much larger than the source it came from and a run touches tens of thousands of
/// classes, so anything that parses a class it does not own has to release it — and only if it was
/// what parsed it, since releasing a tree the caller upstream is still using costs that caller a
/// re-parse. Written out by hand at each site, the two halves came apart: the places reaching for
/// <em>other</em> classes, up an extends chain or along a type alias, were exactly the ones that
/// kept them.</para>
/// </summary>
public class ParseTreeBorrowingTests
{
    private static DirectedGraph GraphOf(params ModelNode[] models)
    {
        var graph = new DirectedGraph();
        foreach (var m in models)
            graph.AddNode(m);
        return graph;
    }

    private static ModelNode Node(string id, string code, string classType = "model")
        => new(id, id, code) { ClassType = classType };

    // ---- the primitive -------------------------------------------------------------------------

    [Fact]
    public void Borrow_ReleasesATreeItParsedItself()
    {
        var node = Node("A", "model A \"d\" end A;");
        Assert.Null(node.Definition.ParsedCode);

        var seen = node.Definition.Borrow(tree => tree is not null);

        Assert.True(seen);
        Assert.Null(node.Definition.ParsedCode);
    }

    [Fact]
    public void Borrow_LeavesATreeTheCallerAlreadyHad()
    {
        var node = Node("A", "model A \"d\" end A;");
        var owned = node.Definition.EnsureParsed();

        node.Definition.Borrow(_ => { });

        // Releasing here would cost the caller the re-parse it was avoiding.
        Assert.Same(owned, node.Definition.ParsedCode);
    }

    [Fact]
    public void Borrow_ReleasesEvenWhenTheWorkThrows()
    {
        var node = Node("A", "model A \"d\" end A;");

        Assert.Throws<InvalidOperationException>(
            () => node.Definition.Borrow<bool>(_ => throw new InvalidOperationException()));

        Assert.Null(node.Definition.ParsedCode);
    }

    [Fact]
    public void Borrow_AnswersWithTheFallbackForSourceThatWillNotParse()
    {
        var node = Node("A", "");

        Assert.Equal(-1, node.Definition.Borrow(_ => 1, ifUnparseable: -1));
    }

    // ---- the walks that reach for other classes ------------------------------------------------

    private const string BaseClass = """
        model Base "A base"
          parameter Real gain = 1 "Gain";
        end Base;
        """;

    private const string Derived = """
        model Derived "Derived"
          extends Base;
        end Derived;
        """;

    [Fact]
    public void ResolvingInheritedElements_DoesNotKeepTheBaseClassesParsed()
    {
        var basis = Node("Base", BaseClass);
        var derived = Node("Derived", Derived);
        var graph = GraphOf(basis, derived);

        var elements = ClassElementResolver.Collect(
            graph, derived, includeProtected: true, includeInherited: true);

        Assert.Contains(elements, e => e.Element.Name == "gain");   // the walk really happened
        Assert.Null(basis.Definition.ParsedCode);
        Assert.Null(derived.Definition.ParsedCode);
    }

    [Fact]
    public void ResolvingInheritedElements_LeavesTheQueriedClassAloneIfTheCallerHeldIt()
    {
        var basis = Node("Base", BaseClass);
        var derived = Node("Derived", Derived);
        var graph = GraphOf(basis, derived);
        var held = derived.Definition.EnsureParsed();

        ClassElementResolver.Collect(graph, derived, includeProtected: true, includeInherited: true);

        Assert.Same(held, derived.Definition.ParsedCode);   // the caller's tree, left alone
        Assert.Null(basis.Definition.ParsedCode);           // the walk's own, handed back
    }

    [Fact]
    public void ResolvingAUnitThroughATypeAlias_DoesNotKeepTheAliasChainParsed()
    {
        var si = Node("Temperature", "type Temperature = Real(unit=\"K\");", classType: "type");
        var alias = Node("T2", "type T2 = Temperature;", classType: "type");
        var graph = GraphOf(si, alias);
        var cache = new ConcurrentDictionary<string, (bool, bool)>(StringComparer.Ordinal);

        var (isReal, hasUnit) = UnitResolver.Resolve(graph, "T2", "T2", [], cache);

        Assert.True(isReal);
        Assert.True(hasUnit);            // the chain really was walked
        Assert.Null(si.Definition.ParsedCode);
        Assert.Null(alias.Definition.ParsedCode);
    }

    [Fact]
    public void ReadingSuppressionsForAGraphFinding_DoesNotKeepTheClassParsed()
    {
        // The graph analyses run after the per-class check has released everything it read, so a
        // class re-parsed here to read its waivers and then kept is held for the rest of the run —
        // once for every class carrying a graph finding.
        var package = Node("Lib", """
            package Lib "L"
              annotation(__MLQT(suppress="Structure.PackageOrder", reason="deliberate"));
            end Lib;
            """, classType: "package");
        var graph = GraphOf(package);
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(RuleIds.PackageOrder, true);

        GraphAnalysisRunner.Run(new GraphAnalysisContext(graph, settings, [package], false));

        Assert.Null(package.Definition.ParsedCode);
    }

    // ---- the walks over every class, which must not take a tree they were merely handed ---------

    /// <summary>
    /// The analyzers walk the whole graph and used to release unconditionally — the other half of the
    /// convention going wrong. A caller holding a tree while an analysis runs (the GUI checks and
    /// analyses in one pass) lost it and paid the re-parse.
    /// </summary>
    [Theory]
    [InlineData("shadowing")]
    [InlineData("unused-members")]
    [InlineData("unused-imports")]
    public void AGraphAnalysis_LeavesATreeItsCallerWasHolding(string analysis)
    {
        var basis = Node("Base", BaseClass);
        var derived = Node("Derived", """
            model Derived "Derived"
              extends Base;
              import Modelica.Units.SI;
            protected
              Real unused;
            end Derived;
            """);
        var graph = GraphOf(basis, derived);
        var held = derived.Definition.EnsureParsed();

        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(analysis switch
        {
            "shadowing" => RuleIds.ShadowingInheritedMember,
            "unused-members" => RuleIds.UnusedMember,
            _ => RuleIds.UnusedImport,
        }, true);

        GraphAnalysisRunner.Run(new GraphAnalysisContext(graph, settings, [basis, derived], true));

        Assert.Same(held, derived.Definition.ParsedCode);   // the caller's tree, left alone
        Assert.Null(basis.Definition.ParsedCode);           // the walk's own, handed back
    }

    [Fact]
    public void MeasuringCoverage_LeavesATreeItsCallerWasHolding()
    {
        // Style checking measures while it still holds the tree it is checking — that is the whole
        // point of measuring there — so the measurer must not be what releases it.
        var node = Node("Base", BaseClass);
        var graph = GraphOf(node);
        var held = node.Definition.EnsureParsed();

        new CoverageMeasurer(graph).Measure(node);

        Assert.Same(held, node.Definition.ParsedCode);
        Assert.NotNull(node.Definition.Coverage);
    }

    [Fact]
    public void MeasuringCoverage_ReleasesATreeItParsedItself()
    {
        // And the dashboard's own sweep, over classes no check reached, must hand them back.
        var node = Node("Base", BaseClass);

        new CoverageMeasurer(GraphOf(node)).Measure(node);

        Assert.Null(node.Definition.ParsedCode);
        Assert.NotNull(node.Definition.Coverage);
    }

    [Fact]
    public void TheInheritedIconWalk_DoesNotKeepTheBaseClassesParsed()
    {
        // Walks up an extends chain looking for an Icon annotation, caching the verdict — so every
        // class beyond the first is one nobody asked for.
        var icons = Node("Icons", """
            partial class Icons "Icons"
              annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
            end Icons;
            """, classType: "class");
        var user = Node("User", "model User \"U\"\n  extends Icons;\nend User;");
        var graph = GraphOf(icons, user);

        var hasIcon = StyleChecking.CreateBaseClassHasIconCallback(graph);
        Assert.NotNull(hasIcon);

        Assert.True(hasIcon("Icons", "User"));
        Assert.Null(icons.Definition.ParsedCode);
        Assert.Null(user.Definition.ParsedCode);
    }
}
