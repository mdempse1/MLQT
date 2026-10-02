using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Renaming a class renames every reference that spells its name - including the ones to what is
/// inside it: <c>Pkg.State</c>, <c>Root.Src.Pkg.State</c>, <c>Pkg.k</c>, an import of
/// <c>Root.Src.Pkg.State</c>.
/// </summary>
/// <remarks>
/// <c>rename_class</c> looked only for references to the class itself, so renaming a package left
/// every reference to a class inside it naming a class that no longer existed, and said so in its
/// result note as "deep member accesses are not rewritten".
/// </remarks>
public class RenameClassContentsTests
{
    private const string Package = """
        within;
        package Root "root"
          package Src
            package Pkg
              constant Real k = 1;
              record State
              end State;
              model UsesItsOwn
                State own;
              end UsesItsOwn;
            end Pkg;
            model Other
              Root.Src.Pkg.State t;
              Pkg.State u;
              Real x = Pkg.k;
            end Other;
            model Importing
              import Root.Src.Pkg.State;
              import P = Root.Src.Pkg;
              State v;
              P.State w;
            end Importing;
            model Base
              replaceable package Medium
                record State
                end State;
              end Medium;
            end Base;
            model Derived
              extends Base;
              Medium.State s;
            end Derived;
          end Src;
        end Root;
        """;

    // Other declares a Box of its own and a component called Box2; a top-level Box3 is further out
    // than Pkg; Src holds a constant Box4.
    private const string Shadowed = """
        within;
        package Root "root"
          package Src
            constant Real Box4 = 1;
            package Pkg
              record State
              end State;
            end Pkg;
            model Other
              package Box
                record State
                end State;
              end Box;
              Real Box2;
              Pkg.State u;
            end Other;
          end Src;
          package Box3
          end Box3;
        end Root;
        """;

    [Theory]
    [InlineData("Box", "Root.Src.Other.Box")]
    [InlineData("Box2", "component Box2 of Root.Src.Other")]
    [InlineData("Box4", "component Box4 of Root.Src")]
    public async Task ANewNameTakenWhereAReferenceIsWritten_IsRefused_AndNothingChanges(string newName, string capturer)
    {
        // `Pkg.State u` would become `<newName>.State u`, and in Other that name already means
        // something else: the file would parse, the rename succeed, and u change type.
        using var host = new TestHost();
        var edit = await Load(host, Shadowed);
        var before = Code(host, "Root.Src.Other");

        var error = Assert.IsType<ToolError>(await edit.RenameClass("Root.Src.Pkg", newName));

        Assert.Contains(capturer, error.Error);
        Assert.Contains("Nothing was changed", error.Error);
        Assert.NotNull(host.Libraries.GetModelById("Root.Src.Pkg"));
        Assert.Equal(before, Code(host, "Root.Src.Other"));
    }

    [Fact]
    public async Task TheRefusalCountsReferences_AndReportsAClashWithTheParentApart()
    {
        using var host = new TestHost();
        var edit = await Load(host, Shadowed);

        // Box: only Other's reference. Box4: Src's own constant clashes with the renamed class, and
        // Other's reference finds it too - one clash, one reference, counted apart.
        var box = Assert.IsType<ToolError>(await edit.RenameClass("Root.Src.Pkg", "Box")).Error;
        var box4 = Assert.IsType<ToolError>(await edit.RenameClass("Root.Src.Pkg", "Box4")).Error;

        Assert.Contains("1 reference(s) would change meaning", box);
        Assert.DoesNotContain("already has", box);
        Assert.Contains("Root.Src already has component Box4 of Root.Src", box4);
        Assert.Contains("1 reference(s) would change meaning", box4);
    }

    [Fact]
    public async Task ANewNameTakenOnlyFurtherOut_IsNoObstacle()
    {
        // Root.Box3 is further out than Root.Src, where the renamed class is found first.
        using var host = new TestHost();
        var edit = await Load(host, Shadowed);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Src.Pkg", "Box3"));

        Assert.Contains("Box3.State u;", Code(host, "Root.Src.Other"));
    }

    private static async Task<EditTools> Load(TestHost h, string package = Package)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = package });
        await h.Libraries.AddLibraryFromDirectoryAsync(dir);
        await new DependencyTools(h.Libraries, h.Impact, h.Resources, h.Session).AnalyzeDependencies();
        return new EditTools(h.Libraries, h.Resources, h.Session);
    }

    private static string Code(TestHost h, string id) =>
        h.Libraries.GetModelById(id)!.Definition.ModelicaCode.Replace("\r\n", "\n");

    [Fact]
    public async Task AReferenceToAClassInside_IsRenamed_QualifiedOrRelative()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Src.Pkg", "Box"));

        var other = Code(host, "Root.Src.Other");
        Assert.Contains("Root.Src.Box.State t;", other);
        Assert.Contains("Box.State u;", other);
        Assert.DoesNotContain("Pkg", other);
    }

    [Fact]
    public async Task AComponentReachedThroughTheClass_IsRenamed()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Src.Pkg", "Box"));

        Assert.Contains("Real x = Box.k;", Code(host, "Root.Src.Other"));
    }

    [Fact]
    public async Task AnImportIsRenamed_AndWhatUsesItThroughAnAliasIsLeftAlone()
    {
        // `State v` and `P.State w` do not spell Pkg; the import clauses that make them work do.
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Src.Pkg", "Box"));

        var importing = Code(host, "Root.Src.Importing");
        Assert.Contains("import Root.Src.Box.State;", importing);
        Assert.Contains("import P = Root.Src.Box;", importing);
        Assert.Contains("State v;", importing);
        Assert.Contains("P.State w;", importing);
    }

    [Fact]
    public async Task ARelativeNameInsideTheClass_IsLeftAlone()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Src.Pkg", "Box"));

        Assert.Contains("State own;", Code(host, "Root.Src.Box.UsesItsOwn"));
    }

    [Fact]
    public async Task ANameReachedThroughInheritance_IsRenamedAndStaysRelative()
    {
        // A rename rewrites the one segment, so `Medium.State` becomes `Fluid.State` - not the base's
        // full name, which would fix it to the base's default and undo a redeclare.
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Src.Base.Medium", "Fluid"));

        var derived = Code(host, "Root.Src.Derived");
        Assert.Contains("Fluid.State s;", derived);
        Assert.DoesNotContain("Base.Fluid", derived);
    }

    [Fact]
    public async Task ASameNamedSegmentThatIsNotTheClass_IsLeftAlone()
    {
        // Renaming Base.Medium.State touches `State` where it names that record, and never the Pkg
        // record of the same name.
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Src.Base.Medium.State", "Condition"));

        Assert.Contains("Medium.Condition s;", Code(host, "Root.Src.Derived"));
        var other = Code(host, "Root.Src.Other");
        Assert.Contains("Root.Src.Pkg.State t;", other);
        Assert.Contains("Pkg.State u;", other);
    }
}
