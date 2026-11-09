using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// mlqt_refactor_class's rename_component: a component renamed with every place that names it, and
/// nothing else that happens to be spelled the same. Renaming parameters to satisfy a naming rule took a
/// replace in each class and every caller by hand; a replace-all on one name could not be done for a
/// class it would also have hit, and a hand rename that chose a name already declared surfaced only when
/// the model failed to build.
/// </summary>
public class RenameComponentTests
{
    private const string Package = """
        within;
        package R "r"
          model Wheel "declares Fz"
            parameter Real Fz = 1 "load";
            parameter Real FzTable = 2 "a longer name that starts the same";
            parameter Boolean useFz = true annotation (Dialog(enable = Fz > 0));
            Real y;
          equation
            y = Fz*2;
          end Wheel;

          model BigWheel "inherits it, and sets it"
            extends Wheel(Fz = 3);
            Real z = Fz + 1;
          end BigWheel;

          model Axle "reaches it through components"
            Wheel wheel(Fz = 4);
            BigWheel big(Fz = 5);
            Real s = wheel.Fz + big.Fz;
          end Axle;

          model Car "through a component of a component"
            Axle axle(wheel(Fz = 6));
            Real t = axle.wheel.Fz;
          end Car;

          model Other "an Fz of its own"
            parameter Real Fz = 9;
            Real u = Fz;
          end Other;

          model Counted "an iterator called Fz"
            extends Wheel;
            Real a[2];
          equation
            for Fz in 1:2 loop
              a[Fz] = Fz;
            end for;
          end Counted;

          model Looped "an iterator called i, reading the parameter"
            extends Wheel;
            Real b[2];
          equation
            for i in 1:2 loop
              b[i] = Fz;
            end for;
          end Looped;

          record Load "a record field"
            Real Fz;
          end Load;

          model UsesLoad "passes it by name"
            Load l = Load(Fz = 1);
          end UsesLoad;
        end R;
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
    public async Task EveryPlaceThatNamesIt_IsRenamed()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        var res = ToolAssert.Ok<RenameComponentResult>(await edit.RenameComponent("R.Wheel", "Fz", "load"));

        Assert.True(res.Changed);
        var wheel = Source(host, "R.Wheel");
        Assert.Contains("parameter Real load = 1", wheel);
        Assert.Contains("y = load*2;", wheel);
        Assert.Contains("Dialog(enable = load > 0)", wheel);             // an annotation's expression
        Assert.Contains("extends Wheel(load = 3);", Source(host, "R.BigWheel"));
        Assert.Contains("Real z = load + 1;", Source(host, "R.BigWheel"));  // inherited
        Assert.Contains("Wheel wheel(load = 4);", Source(host, "R.Axle"));
        Assert.Contains("BigWheel big(load = 5);", Source(host, "R.Axle"));
        Assert.Contains("Real s = wheel.load + big.load;", Source(host, "R.Axle"));
        Assert.Contains("Axle axle(wheel(load = 6));", Source(host, "R.Car")); // nested modification
        Assert.Contains("Real t = axle.wheel.load;", Source(host, "R.Car"));
        Assert.Contains("b[i] = load;", Source(host, "R.Looped"));
    }

    [Fact]
    public async Task WhatIsOnlySpelledTheSame_IsLeftAlone()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameComponentResult>(await edit.RenameComponent("R.Wheel", "Fz", "load"));

        var wheel = Source(host, "R.Wheel");
        Assert.Contains("parameter Real FzTable = 2", wheel);   // a longer name
        Assert.Contains("parameter Boolean useFz", wheel);      // a name containing it
        Assert.Contains("parameter Real Fz = 9;", Source(host, "R.Other"));
        Assert.Contains("Real u = Fz;", Source(host, "R.Other"));          // another class's own Fz
        Assert.Contains("for Fz in 1:2 loop", Source(host, "R.Counted"));
        Assert.Contains("a[Fz] = Fz;", Source(host, "R.Counted"));         // the iterator, not the parameter
        Assert.Contains("Real Fz;", Source(host, "R.Load"));                // the record's field
    }

    [Fact]
    public async Task ARecordField_IsRenamedInTheNamedArgumentsThatPassIt()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        ToolAssert.Ok<RenameComponentResult>(await edit.RenameComponent("R.Load", "Fz", "force"));

        Assert.Contains("Real force;", Source(host, "R.Load"));
        Assert.Contains("Load l = Load(force = 1);", Source(host, "R.UsesLoad"));
        Assert.Contains("parameter Real Fz = 1", Source(host, "R.Wheel"));  // Wheel's is another element
    }

    [Fact]
    public async Task ANameAlreadyDeclaredInTheClass_IsRefused()
    {
        using var host = new TestHost();
        var edit = await Load(host);
        var before = Source(host, "R.Wheel");

        var err = ToolAssert.Error(await edit.RenameComponent("R.Wheel", "Fz", "FzTable"));

        Assert.Contains("already declared", err.Error);
        Assert.Equal(before, Source(host, "R.Wheel"));
    }

    [Fact]
    public async Task ANameADerivedClassDeclares_IsRefused()
    {
        // BigWheel's own z would be declared twice once Wheel's Fz were called z.
        using var host = new TestHost();
        var edit = await Load(host);

        var err = ToolAssert.Error(await edit.RenameComponent("R.Wheel", "Fz", "z"));

        Assert.Contains("'R.BigWheel'", err.Error);
        Assert.Contains("parameter Real Fz = 1", Source(host, "R.Wheel"));
    }

    [Fact]
    public async Task ANameAnIteratorWouldTakeOver_IsRefused()
    {
        // In Looped, `b[i] = Fz` inside `for i` would read the iterator once Fz were called i.
        using var host = new TestHost();
        var edit = await Load(host);

        var err = ToolAssert.Error(await edit.RenameComponent("R.Wheel", "Fz", "i"));

        Assert.Contains("iterator", err.Error);
        Assert.Contains("R.Looped", err.Error);
    }

    [Fact]
    public async Task AnInheritedComponent_IsRenamedWhereItIsDeclared()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        var err = ToolAssert.Error(await edit.RenameComponent("R.BigWheel", "Fz", "load"));

        Assert.Contains("declared in 'R.Wheel'", err.Error);
    }

    [Fact]
    public async Task AClassOrAnUnknownName_IsRefused()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        Assert.Contains("no component named", ToolAssert.Error(await edit.RenameComponent("R.Wheel", "nope", "x")).Error);
        Assert.Contains("not a valid Modelica identifier", ToolAssert.Error(await edit.RenameComponent("R.Wheel", "Fz", "2x")).Error);
        Assert.Contains("same as the current name", ToolAssert.Error(await edit.RenameComponent("R.Wheel", "Fz", "Fz")).Error);
    }

    [Fact]
    public async Task APreview_ShowsEveryFile_AndWritesNothing()
    {
        using var host = new TestHost();
        var edit = await Load(host);
        var before = Source(host, "R.Axle");

        var res = ToolAssert.Ok<RenameComponentResult>(await edit.RenameComponent("R.Wheel", "Fz", "load", preview: true));

        Assert.True(res.PreviewOnly);
        Assert.Contains("wheel.load + big.load", res.Changes.Single().NewContent);
        // Wheel 3 (declaration, Dialog, equation), BigWheel 2, Axle 4, Car 2, Looped 1.
        Assert.Equal(12, res.TotalReplacements);
        Assert.Equal(before, Source(host, "R.Axle"));
    }

    [Fact]
    public async Task TheFacade_RunsTheAnalysisItNeeds_AndNamesWhatIsMissing()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package.ReplaceLineEndings("\n") });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var facade = new ClassFacadeTools(host.Libraries, host.Resources, host.Impact, host.Session);

        Assert.Contains("needs component", ToolAssert.Error(await facade.RefactorClass("R.Wheel", "rename_component", newName: "load")).Error);
        Assert.Contains("needs newName", ToolAssert.Error(await facade.RefactorClass("R.Wheel", "rename_component", component: "Fz")).Error);
        ToolAssert.Ok<RenameComponentResult>(await facade.RefactorClass("R.Wheel", "rename_component", newName: "load", component: "Fz"));
        Assert.Contains("wheel.load", Source(host, "R.Axle"));
    }
}
