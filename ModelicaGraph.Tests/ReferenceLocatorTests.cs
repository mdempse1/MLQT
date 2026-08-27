using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph.Tests;

public class ReferenceLocatorTests
{
    // A graph with P.Base, P.Middle, P.Other registered, so references resolve.
    private static DirectedGraph GraphWith(params string[] ids)
    {
        var graph = new DirectedGraph();
        foreach (var id in ids)
        {
            var leaf = id.Split('.')[^1];
            graph.AddNode(new ModelNode(id, leaf, $"model {leaf}\nend {leaf};"));
        }
        return graph;
    }

    private static IReadOnlyList<ReferenceSite> Locate(string code, DirectedGraph graph, params string[] targets)
        => ReferenceLocator.Locate(graph, ModelicaParserHelper.Parse(code), targets.Length == 0 ? null : targets);

    [Fact]
    public void LocatesQualifiedReference_LeafSpanIsCorrect()
    {
        var graph = GraphWith("P.Base", "P.User");
        const string code = "within P;\nmodel User\n  P.Base b;\nend User;";

        var sites = Locate(code, graph, "P.Base");
        var site = Assert.Single(sites);
        Assert.Equal("P.Base", site.TargetId);
        Assert.Equal("Base", site.Leaf.Text);
        // The leaf span must select exactly "Base" in the source.
        Assert.Equal("Base", code.Substring(site.Leaf.StartIndex, site.Leaf.StopIndex - site.Leaf.StartIndex + 1));
    }

    [Fact]
    public void LocatesRelativeReference()
    {
        var graph = GraphWith("P.Base", "P.User");
        const string code = "within P;\nmodel User\n  Base b;\nend User;";

        var site = Assert.Single(Locate(code, graph, "P.Base"));
        Assert.Equal("P.Base", site.TargetId);
        Assert.Equal("Base", site.Leaf.Text);
        Assert.Single(site.Segments);
    }

    [Fact]
    public void LocatesExtendsReference()
    {
        var graph = GraphWith("P.Base", "P.User");
        const string code = "within P;\nmodel User\n  extends P.Base;\nend User;";

        var site = Assert.Single(Locate(code, graph, "P.Base"));
        Assert.Equal("P.Base", site.TargetId);
    }

    [Fact]
    public void DoesNotMatchDifferentClassWithSameLeaf()
    {
        // Two 'Base' classes in different packages; a reference to Q.Base must not match P.Base.
        var graph = GraphWith("P.Base", "Q.Base", "Q.User");
        const string code = "within Q;\nmodel User\n  Base b;\nend User;";

        var sites = Locate(code, graph, "P.Base");
        Assert.Empty(sites); // resolves to Q.Base, not the P.Base target
    }

    [Fact]
    public void AliasUsage_LeafIsAlias_NotClassName()
    {
        // 'import Bee = P.Base' then 'Bee b': the import statement references Base; the usage uses 'Bee'.
        var graph = GraphWith("P.Base", "Q.User");
        const string code = "within Q;\nmodel User\n  import Bee = P.Base;\n  Bee b;\nend User;";

        var sites = Locate(code, graph, "P.Base");
        // Both the import's 'P.Base' and the alias usage 'Bee' resolve to P.Base.
        Assert.Equal(2, sites.Count);
        Assert.Contains(sites, s => s.Leaf.Text == "Base");  // import statement — a rename would edit this
        Assert.Contains(sites, s => s.Leaf.Text == "Bee");   // alias usage — a leaf rename would skip this
    }

    [Fact]
    public void ScopeAware_NestedClassesResolveIndependently()
    {
        var graph = GraphWith("P.Base", "P.Outer", "P.Outer.Inner");
        const string code =
            "within P;\npackage Outer\n  model Inner\n    P.Base b;\n  end Inner;\nend Outer;";

        var site = Assert.Single(Locate(code, graph, "P.Base"));
        Assert.Equal("P.Base", site.TargetId);
    }

    [Fact]
    public void NullTargets_RecordsAllResolvableReferences()
    {
        var graph = GraphWith("P.Base", "P.Other", "P.User");
        const string code = "within P;\nmodel User\n  Base b;\n  Other o;\n  Real r;\nend User;";

        var sites = Locate(code, graph); // all resolvable
        Assert.Contains(sites, s => s.TargetId == "P.Base");
        Assert.Contains(sites, s => s.TargetId == "P.Other");
        Assert.DoesNotContain(sites, s => s.Text == "Real"); // built-in, unresolved
    }

    [Fact]
    public void MultipleReferences_AllLocated()
    {
        var graph = GraphWith("P.Base", "P.User");
        const string code = "within P;\nmodel User\n  P.Base b1;\n  P.Base b2;\nend User;";

        var sites = Locate(code, graph, "P.Base");
        Assert.Equal(2, sites.Count);
        Assert.All(sites, s => Assert.Equal("Base",
            code.Substring(s.Leaf.StartIndex, s.Leaf.StopIndex - s.Leaf.StartIndex + 1)));
    }

    // ── the class's own declaration, which a rename has to rewrite too ──

    private static IReadOnlyList<DefinitionSite> Definitions(
        string code, DirectedGraph graph, params string[] targets)
    {
        var locator = new ReferenceLocator(graph, targets.Length == 0 ? null : targets);
        locator.Visit(ModelicaParserHelper.Parse(code));
        return locator.Definitions;
    }

    [Fact]
    public void ADefinitionIsNotAReference()
    {
        // `model User` declares the class; it is not a usage of it. Counting it would make a rename
        // rewrite the declaration twice and an impact count report the class as its own user.
        var graph = GraphWith("P.User");
        const string code = "within P;\nmodel User\n  Real x;\nend User;";

        Assert.Empty(Locate(code, graph, "P.User"));
        Assert.Equal("P.User", Assert.Single(Definitions(code, graph, "P.User")).Id);
    }

    [Fact]
    public void ADefinitionSpansTheWholeClass()
    {
        // The span is what a move cuts out. It runs from the class keyword to the closing name;
        // the separating semicolon belongs to the enclosing list, so a caller splicing the class
        // out has to take that with it.
        var graph = GraphWith("P.User");
        const string code = "within P;\nmodel User \"a user\"\n  Real x;\nend User;";

        var definition = Assert.Single(Definitions(code, graph, "P.User"));

        Assert.Equal("model User \"a user\"\n  Real x;\nend User",
            code[definition.StartIndex..(definition.StopIndex + 1)]);
        Assert.Equal(';', code[definition.StopIndex + 1]);
        Assert.Equal(2, definition.Line);
    }

    [Fact]
    public void BothNameTokensOfAClassAreLocated()
    {
        // `model X … end X;` names the class twice, and a rename that rewrote only the first would
        // leave source that does not compile.
        var graph = GraphWith("P.User");
        const string code = "within P;\nmodel User\n  Real x;\nend User;";

        var definition = Assert.Single(Definitions(code, graph, "P.User"));

        Assert.Equal(2, definition.NameTokens.Count);
        Assert.All(definition.NameTokens, token => Assert.Equal(
            "User", code[token.StartIndex..(token.StopIndex + 1)]));
    }

    [Fact]
    public void ANestedClassIsDefinedByItsFullyQualifiedId()
    {
        var graph = GraphWith("P.Outer", "P.Outer.Inner");
        const string code = "within P;\npackage Outer\n  model Inner\n    Real x;\n  end Inner;\nend Outer;";

        var definition = Assert.Single(Definitions(code, graph, "P.Outer.Inner"));

        Assert.Equal("P.Outer.Inner", definition.Id);
        Assert.Equal(3, definition.Line);
    }

    [Fact]
    public void WithNoTargets_EveryClassInTheFileIsADefinition()
    {
        var graph = GraphWith("P.Outer", "P.Outer.Inner");
        const string code = "within P;\npackage Outer\n  model Inner\n    Real x;\n  end Inner;\nend Outer;";

        Assert.Equal(["P.Outer", "P.Outer.Inner"], Definitions(code, graph).Select(d => d.Id));
    }
}
