using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The shapes a review of rename_component found it got wrong: a constraining clause's modification and
/// a redeclaration left naming the old element, a name something else already meant taken over without
/// a word, inner/outer partners cut apart, and an iterator refusal that refused what no iterator reaches.
/// </summary>
public class RenameComponentEdgeTests
{
    private const string Package = """
        within;
        package E "e"
          constant Real g = 9.81 "the package's constant";

          model Wheel "w"
            parameter Real Fz = 1;
            parameter Real x = 2;
            Real y = g;
          end Wheel;

          model User "constrainedby"
            replaceable Wheel w(Fz = 1) constrainedby Wheel(Fz = 2);
          end User;

          model Redeclared "a redeclare in a modification"
            Wheel w2(redeclare Real Fz = 3);
          end Redeclared;

          model Heavy "redeclares it in its body"
            extends Wheel;
            redeclare Real Fz = 5;
            Real heavier = Fz*2;
          end Heavy;

          model Dotted "an iterator of the new name, and a dotted reference only"
            Wheel w;
            Real c[2];
          equation
            for load in 1:2 loop
              c[load] = w.Fz;
            end for;
          end Dotted;

          model ImportUser "an import alias"
            import W = E.Wheel;
            W wheel;
            parameter Real p = 1;
          end ImportUser;

          package Consts "a constant and a function nested beside it"
            constant Real a = 1;
            function f "uses a, and has an input b"
              input Real b;
              output Real r = a*b;
            end f;
          end Consts;

          model World "w"
          end World;
          model Top "declares the inner"
            inner World world;
          end Top;
        end E;
        """;

    private static async Task<EditTools> Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package.ReplaceLineEndings("\n") });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        await new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session).AnalyzeDependencies();
        return new EditTools(host.Libraries, host.Resources, host.Session);
    }

    private static string Source(TestHost host, string id) => host.Libraries.GetModelById(id)!.Definition.ModelicaCode!;

    [Fact]
    public async Task AConstrainingClausesModification_IsRenamedToo()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameComponentResult>(await edit.RenameComponent("E.Wheel", "Fz", "load"));

        Assert.Contains("replaceable Wheel w(load = 1) constrainedby Wheel(load = 2);", Source(host, "E.User"));
    }

    [Fact]
    public async Task Redeclarations_InAModificationAndInADerivedClass_AreRenamed_WithWhatReadsThem()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameComponentResult>(await edit.RenameComponent("E.Wheel", "Fz", "load"));

        Assert.Contains("Wheel w2(redeclare Real load = 3);", Source(host, "E.Redeclared"));
        Assert.Contains("redeclare Real load = 5;", Source(host, "E.Heavy"));
        Assert.Contains("Real heavier = load*2;", Source(host, "E.Heavy"));
    }

    [Fact]
    public async Task ANameThePackagesConstantHas_IsRefused_ForTheClassReadsIt()
    {
        // Wheel's `y = g` reads E's constant; with x called g it would read the component.
        using var host = new TestHost();
        var edit = await Load(host);

        var err = ToolAssert.Error(await edit.RenameComponent("E.Wheel", "x", "g"));

        Assert.Contains("already means something else", err.Error);
        Assert.Contains("E.Wheel", err.Error);
        Assert.Contains("parameter Real x = 2;", Source(host, "E.Wheel"));
    }

    [Fact]
    public async Task AnImportAliasName_IsRefused()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        var err = ToolAssert.Error(await edit.RenameComponent("E.ImportUser", "p", "W"));

        Assert.Contains("already means something else", err.Error);
    }

    [Fact]
    public async Task AConstantRenamedToANameANestedClassDeclares_IsRefused()
    {
        // f's `a` would become its own input `b`.
        using var host = new TestHost();
        var edit = await Load(host);

        var err = ToolAssert.Error(await edit.RenameComponent("E.Consts", "a", "b"));

        Assert.Contains("E.Consts.f", err.Error);
        Assert.Contains("output Real r = a*b;", Source(host, "E.Consts.f"));
    }

    [Fact]
    public async Task AnInnerComponent_IsRefused_ForItsOutersFindItByName()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        var err = ToolAssert.Error(await edit.RenameComponent("E.Top", "world", "env"));

        Assert.Contains("inner", err.Error);
        Assert.Contains("inner World world;", Source(host, "E.Top"));
    }

    [Fact]
    public async Task AnIteratorOfTheNewName_DoesNotRefuseADottedReference_ItCannotReach()
    {
        // `w.Fz` is a member of w's type: an iterator called load does not touch it.
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameComponentResult>(await edit.RenameComponent("E.Wheel", "Fz", "load"));

        Assert.Contains("c[load] = w.load;", Source(host, "E.Dotted"));
    }
}
