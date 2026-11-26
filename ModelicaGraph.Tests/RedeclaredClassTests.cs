using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <see cref="RedeclaredClass.Of"/>: the class a component is after the redeclarations that reach it,
/// which is the class a diagram draws for it.
/// </summary>
public class RedeclaredClassTests
{
    private static readonly string Source = """
        within;
        package T "t"
          partial model PartialEngine "constraining"
          end PartialEngine;
          model Diesel "concrete"
            extends PartialEngine;
          end Diesel;
          model Petrol "another"
            extends PartialEngine;
          end Petrol;
          model Plain "never redeclared"
          end Plain;

          partial model Template "a replaceable component, a replaceable class, a plain component"
            replaceable PartialEngine engine(y = 1) constrainedby PartialEngine;
            replaceable model Gearbox = PartialEngine;
            Gearbox gearbox(b = 3);
            Plain plain;
          end Template;

          model ByExtends
            extends Template(redeclare Diesel engine(x = 5), redeclare model Gearbox = Petrol(a = 1, b = 2));
          end ByExtends;

          model ShortClassRedeclare = Template(redeclare model Gearbox = Diesel);

          model ClassExtendsBody
            extends Template;
            redeclare model extends Gearbox
            end Gearbox;
          end ClassExtendsBody;

          model OverByExtends "a more derived redeclaration wins"
            extends ByExtends(redeclare Petrol engine, redeclare model Gearbox = Diesel);
          end OverByExtends;

          model Deeper "inherits the redeclarations it does not make"
            extends ByExtends;
          end Deeper;

          model InBody
            extends Template;
            redeclare model Gearbox = Diesel;
          end InBody;

          model Short = Template(redeclare Petrol engine) "a short class's redeclaration";

          model NotLoaded
            extends Template(redeclare Missing engine, redeclare model Gearbox = AlsoMissing);
          end NotLoaded;

          model Untouched
            extends Template;
          end Untouched;
        end T;
        """.Replace("\r\n", "\n");

    private readonly DirectedGraph _graph = new();

    public RedeclaredClassTests() =>
        GraphBuilder.LoadModelicaFile(_graph, Path.Combine(Path.GetTempPath(), "RedeclaredClass", "T.mo"), Source);

    private string? ClassOf(string within, string component)
    {
        var node = _graph.GetNode<ModelNode>(within)!;
        var member = ClassElementResolver.Collect(_graph, node, includeProtected: true, includeInherited: true)
            .Single(m => m.Element.Name == component);
        return RedeclaredClass.Of(_graph, node, member)?.Id;
    }

    [Fact]
    public void NothingRedeclared_IsTheDeclaredClass()
    {
        Assert.Equal("T.PartialEngine", ClassOf("T.Untouched", "engine"));
        Assert.Equal("T.Template.Gearbox", ClassOf("T.Untouched", "gearbox"));
        Assert.Equal("T.Plain", ClassOf("T.ByExtends", "plain"));
        Assert.Equal("T.PartialEngine", ClassOf("T.Template", "engine"));
    }

    [Fact]
    public void AnExtendsClausesRedeclarations_ReachTheComponentAndTheReplaceableClass()
    {
        Assert.Equal("T.Diesel", ClassOf("T.ByExtends", "engine"));
        Assert.Equal("T.Petrol", ClassOf("T.ByExtends", "gearbox"));
    }

    [Fact]
    public void TheMostDerivedRedeclarationWins_AndAnInheritedOneStillHolds()
    {
        Assert.Equal("T.Petrol", ClassOf("T.OverByExtends", "engine"));
        Assert.Equal("T.Diesel", ClassOf("T.OverByExtends", "gearbox"));
        Assert.Equal("T.Diesel", ClassOf("T.Deeper", "engine"));
        Assert.Equal("T.Petrol", ClassOf("T.Deeper", "gearbox"));
    }

    [Fact]
    public void AReplaceableClassRedeclaredInTheBody_IsTheNestedClass()
    {
        Assert.Equal("T.InBody.Gearbox", ClassOf("T.InBody", "gearbox"));
        Assert.Equal("T.PartialEngine", ClassOf("T.InBody", "engine"));
    }

    [Fact]
    public void AShortClassesRedeclaration_ReachesTheComponent()
        => Assert.Equal("T.Petrol", ClassOf("T.Short", "engine"));

    [Fact]
    public void ARedeclarationToAClassNotLoaded_IsNull_NotTheDeclaredClass()
    {
        Assert.Null(ClassOf("T.NotLoaded", "engine"));
        Assert.Null(ClassOf("T.NotLoaded", "gearbox"));
    }

    private ComponentClass For(string within, string component)
    {
        var node = _graph.GetNode<ModelNode>(within)!;
        var member = ClassElementResolver.Collect(_graph, node, includeProtected: true, includeInherited: true)
            .Single(m => m.Element.Name == component);
        return RedeclaredClass.For(_graph, node, member)!;
    }

    [Fact]
    public void NothingRedeclared_KeepsTheDeclarationsNameAndModifications()
    {
        var engine = For("T.Untouched", "engine");

        Assert.False(engine.Redeclared);
        Assert.Equal("PartialEngine", engine.Written);
        Assert.Equal(new Dictionary<string, string> { ["y"] = "1" }, engine.Modifications);
    }

    [Fact]
    public void ARedeclaredComponent_TakesTheRedeclarationsModifications_NotTheDeclarations()
    {
        // MLS 7.3: the redeclaration is the whole declaration; `y = 1` went with the one it replaced.
        var engine = For("T.ByExtends", "engine");

        Assert.True(engine.Redeclared);
        Assert.Equal("Diesel", engine.Written);
        Assert.Equal(new Dictionary<string, string> { ["x"] = "5" }, engine.Modifications);
    }

    [Fact]
    public void AClassRedeclarationsModifications_AreDefaults_UnderTheInstancesOwn()
    {
        // `Gearbox = Petrol(a = 1, b = 2)` gives defaults; `gearbox(b = 3)` is what the instance says.
        var gearbox = For("T.ByExtends", "gearbox");

        Assert.True(gearbox.Redeclared);
        Assert.Equal("Petrol", gearbox.Written);
        Assert.Equal(new Dictionary<string, string> { ["a"] = "1", ["b"] = "3" }, gearbox.Modifications);
    }

    [Fact]
    public void ANestedRedeclaredClass_IsWrittenAsTheClassItNames()
    {
        var gearbox = For("T.InBody", "gearbox");

        Assert.Equal("T.InBody.Gearbox", gearbox.Class?.Id);
        Assert.Equal("Diesel", gearbox.Written);
        Assert.Equal(new Dictionary<string, string> { ["b"] = "3" }, gearbox.Modifications);
    }

    [Fact]
    public void AClassExtendsRedeclaration_IsTheNestedClass_WrittenAsItsOwnName()
    {
        var gearbox = For("T.ClassExtendsBody", "gearbox");

        Assert.Equal("T.ClassExtendsBody.Gearbox", gearbox.Class?.Id);
        Assert.Equal("Gearbox", gearbox.Written);
    }

    [Fact]
    public void AShortClassesClassRedeclaration_ReachesTheReplaceableClass()
        => Assert.Equal("T.Diesel", ClassOf("T.ShortClassRedeclare", "gearbox"));

    [Fact]
    public void ARedeclarationToAClassNotLoaded_IsStillNamed()
    {
        var engine = For("T.NotLoaded", "engine");

        Assert.Null(engine.Class);
        Assert.True(engine.Redeclared);
        Assert.Equal("Missing", engine.Written);
    }

    [Fact]
    public void AnElementThatIsNotAComponent_HasNoClass()
    {
        var node = _graph.GetNode<ModelNode>("T.Template")!;
        var nested = ClassElementResolver.Collect(_graph, node, includeProtected: true, includeInherited: true)
            .First(m => m.Element.Kind == ClassElementKind.Class);

        Assert.Null(RedeclaredClass.Of(_graph, node, nested));
        Assert.Null(RedeclaredClass.For(_graph, node, nested));
    }
}
