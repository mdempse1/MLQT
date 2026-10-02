using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// How a name's first segment is bound and what the rest of it means (MLS §5.3): the first segment is
/// looked up, each later one is a member of the class before it, and once the first is found the
/// search is over.
/// </summary>
/// <remarks>
/// Each test is a shape a whole-branch review found the lookup getting wrong: a composite name through
/// a short class or an inherited <c>Medium</c>, an imported constant, a wildcard import answering
/// before a qualified one, a base inherited from an enclosing package, and a quoted identifier with a
/// dot in it.
/// </remarks>
public class NameBindingTests
{
    private static ModelNode Node(string id, string code) => new(id, ModelicaParser.Helpers.ModelicaName.LeafOf(id), code);

    private static DirectedGraph Graph(params (string Id, string Code)[] nodes)
    {
        var graph = new DirectedGraph();
        foreach (var (id, code) in nodes)
            graph.AddNode(Node(id, code));
        return graph;
    }

    private static NameResolution? Lookup(DirectedGraph graph, string ownerId, string name) =>
        TypeResolver.ResolveNamePath(graph, ownerId, name, null, global: false, inherited: true,
            new TypeResolver.AncestorCache());

    // ---- composite names ------------------------------------------------------------------------

    private static DirectedGraph Media() => Graph(
        ("Lib", "package Lib\nend Lib;"),
        ("Lib.Interfaces", "package Interfaces\nend Interfaces;"),
        ("Lib.Interfaces.PartialMedium", "partial package PartialMedium\n  record ThermodynamicState\n  end ThermodynamicState;\nend PartialMedium;"),
        ("Lib.Interfaces.PartialMedium.ThermodynamicState", "record ThermodynamicState\nend ThermodynamicState;"),
        ("Lib.Water", "package Water\n  extends Interfaces.PartialMedium;\nend Water;"),
        // A package's own Medium, which must never answer for the one a class declares or inherits.
        ("Lib.Medium", "package Medium\n  record ThermodynamicState\n  end ThermodynamicState;\n  record Other\n  end Other;\nend Medium;"),
        ("Lib.Medium.ThermodynamicState", "record ThermodynamicState\nend ThermodynamicState;"),
        ("Lib.Medium.Other", "record Other\nend Other;"),
        ("Lib.Base", "partial model Base\n  replaceable package Medium = Lib.Interfaces.PartialMedium;\nend Base;"),
        ("Lib.Base.Medium", "replaceable package Medium = Lib.Interfaces.PartialMedium;"),
        ("Lib.Pipe", "model Pipe\n  extends Base;\n  Medium.ThermodynamicState state;\nend Pipe;"),
        ("Lib.Tank", "model Tank\n  package Medium = Lib.Water;\n  Medium.ThermodynamicState state;\nend Tank;"),
        ("Lib.Tank.Medium", "package Medium = Lib.Water;"));

    [Fact]
    public void ACompositeName_ThroughAnInheritedShortClass_IsAMemberOfItsBase()
    {
        // Base.Medium.ThermodynamicState is no class anyone declared; PartialMedium's is.
        var resolved = Lookup(Media(), "Lib.Pipe", "Medium.ThermodynamicState");

        Assert.Equal("Lib.Interfaces.PartialMedium.ThermodynamicState", resolved?.Node.Id);
        Assert.Equal("Lib.Base.Medium", resolved?.FirstId);
        Assert.Equal(NameBinding.Inherited, resolved?.Binding);
    }

    [Fact]
    public void ACompositeName_ThroughALocalShortClass_FollowsItsBaseChain()
    {
        // Tank.Medium = Water, which extends PartialMedium: two steps to the record.
        var resolved = Lookup(Media(), "Lib.Tank", "Medium.ThermodynamicState");

        Assert.Equal("Lib.Interfaces.PartialMedium.ThermodynamicState", resolved?.Node.Id);
        Assert.Equal(NameBinding.Own, resolved?.Binding);
    }

    [Fact]
    public void OnceTheFirstSegmentIsFound_TheSearchIsOver()
    {
        // Medium binds to the inherited one, which has no Other. Lib.Medium.Other exists, and searching
        // on outward for it gave a package's own Medium the inherited one's place.
        Assert.Null(Lookup(Media(), "Lib.Pipe", "Medium.Other"));
    }

    // ---- imports --------------------------------------------------------------------------------

    private static DirectedGraph Imports(string user) => Graph(
        ("Lib", "package Lib\nend Lib;"),
        ("Lib.A", "package A\nend A;"),
        ("Lib.A.X", "model X\nend X;"),
        ("Lib.B", "package B\nend B;"),
        ("Lib.B.X", "model X\nend X;"),
        ("Lib.Constants", "package Constants\n  constant Real pi = 3.14159;\n  constant Real e = 2.71828;\nend Constants;"),
        ("Lib.User", user));

    [Theory]
    // Whichever is written first: a name a qualified import gives is the one meant (MLS §13.2.1).
    [InlineData("model User\n  import Lib.A.*;\n  import Lib.B.X;\n  X x;\nend User;")]
    [InlineData("model User\n  import Lib.B.X;\n  import Lib.A.*;\n  X x;\nend User;")]
    [InlineData("model User\n  import Lib.A.*;\n  import X = Lib.B.X;\n  X x;\nend User;")]
    public void AQualifiedImport_AnswersBeforeAWildcard(string user)
    {
        var resolved = Lookup(Imports(user), "Lib.User", "X");

        Assert.Equal("Lib.B.X", resolved?.Node.Id);
        Assert.NotEqual(NameBinding.WildcardImport, resolved?.Binding);
    }

    [Fact]
    public void AWildcardImport_IsBoundAsOne()
    {
        var resolved = Lookup(Imports("model User\n  import Lib.A.*;\n  X x;\nend User;"), "Lib.User", "X");

        Assert.Equal("Lib.A.X", resolved?.Node.Id);
        Assert.Equal(NameBinding.WildcardImport, resolved?.Binding);
    }

    [Theory]
    [InlineData("model User\n  import Lib.Constants.pi;\n  Real y = pi;\nend User;", "pi")]
    [InlineData("model User\n  import Lib.Constants.*;\n  Real y = e;\nend User;", "e")]
    [InlineData("model User\n  import Lib.Constants.{pi, e};\n  Real y = e;\nend User;", "e")]
    [InlineData("model User\n  import P = Lib.Constants.pi;\n  Real y = P;\nend User;", "P")]
    public void AnImportedConstant_IsFoundInTheClassThatDeclaresIt(string user, string reference)
    {
        var graph = Imports(user);
        var references = ClassElementResolver.ReferencesIn(graph, graph.GetNode<ModelNode>("Lib.User")!);

        var resolved = references.Resolve(reference);

        Assert.Equal("Lib.Constants", resolved?.Element.OwnerId);
        // ...and it is a use of the class that declares it, so neither reads as unused.
        Assert.Equal("Lib.Constants", references.Start(reference)?.Scope.Id);
    }

    [Fact]
    public void AnImportOfAnEnclosingPackage_LendsItsConstantsToo()
    {
        var graph = Imports("model User\nend User;");
        graph.AddNode(Node("Lib.Pkg", "package Pkg\n  import Lib.Constants.pi;\nend Pkg;"));
        graph.AddNode(Node("Lib.Pkg.M", "model M\n  Real y = pi;\nend M;"));

        var resolved = ClassElementResolver.ReferencesIn(graph, graph.GetNode<ModelNode>("Lib.Pkg.M")!).Resolve("pi");

        Assert.Equal("Lib.Constants", resolved?.Element.OwnerId);
    }

    // ---- bases ----------------------------------------------------------------------------------

    [Fact]
    public void ABase_InheritedByAnEnclosingPackage_IsFound()
    {
        // P extends Q, so PartialThing is one of P's elements, and P.M's `extends PartialThing` means
        // Q.PartialThing. Only the class's *own* inherited elements are hidden from its extends clause.
        var graph = Graph(
            ("Q", "package Q\n  partial model PartialThing\n  end PartialThing;\nend Q;"),
            ("Q.PartialThing", "partial model PartialThing\nend PartialThing;"),
            ("P", "package P\n  extends Q;\nend P;"),
            ("P.M", "model M\n  extends PartialThing;\nend M;"));

        var bases = ClassElementResolver.DirectBases(graph, graph.GetNode<ModelNode>("P.M")!);

        Assert.Equal(["Q.PartialThing"], bases.Select(b => b.Base.Id));
    }

    // ---- quoted identifiers ---------------------------------------------------------------------

    [Fact]
    public void AQuotedIdentifierWithADot_IsOneSegment()
    {
        var graph = Graph(
            ("Lib", "package Lib\nend Lib;"),
            ("Lib.'a.b'", "package 'a.b'\nend 'a.b';"),
            ("Lib.'a.b'.C", "model C\nend C;"),
            ("Lib.M", "model M\n  'a.b'.C c;\nend M;"));

        var resolved = Lookup(graph, "Lib.M", "'a.b'.C");

        Assert.Equal("Lib.'a.b'.C", resolved?.Node.Id);
        Assert.Equal(["Lib.'a.b'", "Lib.'a.b'.C"], resolved?.Path);
    }
}
