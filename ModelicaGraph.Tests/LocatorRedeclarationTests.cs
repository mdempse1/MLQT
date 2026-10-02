using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// What the reference locator reports about a replaceable class beyond the names that spell it: the
/// <c>redeclare</c>s that replace it, and a name that goes <b>through</b> it to a class somewhere else.
/// </summary>
/// <remarks>
/// A rename of <c>Base.Medium</c> has to rewrite <c>redeclare package Medium = Water</c> in every
/// modification of Base and <c>Medium.State</c> wherever it goes through Medium - which, under
/// <c>Medium = Water</c>, ends at <c>Water.State</c>, a class the rename does not target. Neither was
/// reported, so each was left naming an element that no longer existed.
/// </remarks>
public class LocatorRedeclarationTests
{
    private static DirectedGraph Graph()
    {
        var graph = new DirectedGraph();
        void Add(string id, string code) => graph.AddNode(new ModelNode(id, ModelicaName.LeafOf(id), code));
        Add("Lib", "package Lib\nend Lib;");
        Add("Lib.Water", "package Water\n  record State\n  end State;\nend Water;");
        Add("Lib.Water.State", "record State\nend State;");
        Add("Lib.Base", "partial model Base\n  replaceable package Medium = Water;\nend Base;");
        Add("Lib.Base.Medium", "replaceable package Medium = Water;");
        Add("Lib.Holder", "model Holder\n  Base b;\nend Holder;");
        Add("Lib.Other", "model Other\n  replaceable package Medium = Water;\nend Other;");
        Add("Lib.Other.Medium", "replaceable package Medium = Water;");
        return graph;
    }

    // The class visited is in the graph too, as a loaded one always is: its bases are read from there.
    private static ReferenceLocator Visit(string code, params string[] targets)
    {
        var graph = Graph();
        graph.AddNode(new ModelNode("Lib.User", "User", code["within Lib;\n".Length..]));
        var locator = new ReferenceLocator(graph, targets);
        locator.Visit(ModelicaParserHelper.Parse(code));
        return locator;
    }

    private static string Text(string code, NameSegment name) =>
        code.Substring(name.StartIndex, name.StopIndex - name.StartIndex + 1);

    [Theory]
    [InlineData("model User\n  extends Base(redeclare package Medium = Water);\nend User;")]
    [InlineData("model User\n  Base b(redeclare package Medium = Water);\nend User;")]
    [InlineData("model User = Base(redeclare package Medium = Water);")]
    [InlineData("model User\n  extends Base(redeclare replaceable package Medium = Water);\nend User;")]
    [InlineData("model User\n  Holder h(b(redeclare package Medium = Water));\nend User;")]
    public void ARedeclarationOfTheClass_IsReported_WhereverTheModificationIs(string user)
    {
        var code = "within Lib;\n" + user;

        var redeclaration = Assert.Single(Visit(code, "Lib.Base.Medium").Redeclarations);

        Assert.Equal("Lib.Base.Medium", redeclaration.TargetId);
        Assert.Equal("Medium", Text(code, redeclaration.Name));
        Assert.Equal(code[..code.IndexOf("Medium", StringComparison.Ordinal)].Count(c => c == '\n') + 1, redeclaration.Line);
    }

    [Fact]
    public void ARedeclarationOfAnotherClassOfTheSameName_IsNotReported()
    {
        const string code = "within Lib;\nmodel User\n  Other o(redeclare package Medium = Water);\nend User;";

        Assert.Empty(Visit(code, "Lib.Base.Medium").Redeclarations);
        Assert.Equal("Lib.Other.Medium", Assert.Single(Visit(code, "Lib.Other.Medium").Redeclarations).TargetId);
    }

    [Theory]
    [InlineData("  Medium.State s;\n", "Medium")]
    [InlineData("  Lib.Base.Medium.State s;\n", "Lib.Base.Medium")]
    public void ANameThroughTheClass_ToAClassElsewhere_IsASiteForTheSegmentsThatNameIt(string declaration, string site)
    {
        // `Medium.State` ends at Water.State; the site is the part that names Medium.
        var code = "within Lib;\nmodel User\n  extends Base;\n" + declaration + "end User;";

        var found = Assert.Single(Visit(code, "Lib.Base.Medium").Sites);

        Assert.Equal("Lib.Base.Medium", found.TargetId);
        Assert.Equal(site, code.Substring(found.StartIndex, found.StopIndex - found.StartIndex + 1));
        Assert.Equal("Lib.Base.Medium", found.SegmentIds[^1]);
    }

    [Fact]
    public void ANameThatOnlyEndsAtTheSameClass_ThroughSomethingElse_IsNotASite()
    {
        // Water.State is where Medium.State ends too, but nothing here goes through Base.Medium.
        const string code = "within Lib;\nmodel User\n  Water.State s;\nend User;";

        Assert.Empty(Visit(code, "Lib.Base.Medium").Sites);
    }
}
