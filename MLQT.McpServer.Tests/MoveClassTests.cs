using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class MoveClassTests
{
    // Root.Src.{Widget, Sibling, UsesWidget} and an empty Root.Dst. Widget uses Sibling; UsesWidget uses Widget.
    private const string Package = """
        within;
        package Root "root"
          package Src
            model Widget "w"
              Sibling s;
            end Widget;
            model Sibling
              Real y;
            end Sibling;
            model UsesWidget
              Widget w;
            end UsesWidget;
          end Src;
          package Dst
          end Dst;
        end Root;
        """;

    // Root.Src.Base declares a replaceable Medium; Derived extends it and writes Medium.State, and Other
    // names the same record in full.
    private const string InheritedPackage = """
        within;
        package Root "root"
          package Src
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
            model Other
              Root.Src.Base.Medium.State t;
            end Other;
          end Src;
          package Dst
          end Dst;
        end Root;
        """;

    // Moving Root.Src.Widget writes Root.Dst.Widget wherever Widget is used. Shadow declares a component
    // called Root; Sealed is encapsulated and reaches Widget through a wildcard import, which stays
    // behind when Widget moves; OnlyImports just imports it.
    private const string FullNamePackage = """
        within;
        package Root "root"
          package Src
            model Widget
            end Widget;
            model Shadow
              Real Root;
              Widget w;
            end Shadow;
            encapsulated model Sealed
              import Root.Src.*;
              Widget w;
            end Sealed;
            encapsulated model OnlyImports
              import Root.Src.Widget;
            end OnlyImports;
          end Src;
          package Dst
          end Dst;
        end Root;
        """;

    private static async Task<EditTools> LoadFullNames(TestHost h, string package)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        await new DependencyTools(h.Libraries, h.Impact, h.Resources, h.Session).AnalyzeDependencies();
        return new EditTools(h.Libraries, h.Resources, h.Session);
    }

    [Fact]
    public async Task Move_IsRefused_WhereTheFullNameItWritesWouldNotMeanTheClass()
    {
        // In Shadow, Root is the component, so `Root.Dst.Widget w` would not parse as the class at all.
        // In Sealed, `import Root.Src.*` no longer brings Widget in once it has moved, so the name has to
        // be written in full - and nothing outside an encapsulated class is visible, so it would
        // resolve to nothing.
        using var host = new TestHost();
        var edit = await LoadFullNames(host, FullNamePackage);
        var before = host.Libraries.GetModelById("Root.Src.Shadow")!.Definition.ModelicaCode;

        var error = Assert.IsType<ToolError>(await edit.MoveClass("Root.Src.Widget", "Root.Dst")).Error;

        Assert.Contains("component Root of Root.Src.Shadow", error);
        Assert.Contains("Root.Src.Sealed is encapsulated", error);
        Assert.Contains("Nothing was changed", error);
        Assert.NotNull(host.Libraries.GetModelById("Root.Src.Widget"));
        Assert.Equal(before, host.Libraries.GetModelById("Root.Src.Shadow")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task Move_RewritesAnImportInAnEncapsulatedClass_WhichIsLookedUpFromTheTop()
    {
        // OnlyImports names Widget only in its import clause, which is always resolved from the top.
        using var host = new TestHost();
        // Normalised first: a raw string literal carries the checkout's line endings.
        var package = FullNamePackage.Replace("\r\n", "\n")
            .Replace("      Real Root;\n      Widget w;\n", "")
            .Replace("      import Root.Src.*;\n      Widget w;\n", "");
        Assert.DoesNotContain("Real Root;", package);
        Assert.DoesNotContain("Widget w;", package);
        var edit = await LoadFullNames(host, package);

        ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.Widget", "Root.Dst"));

        Assert.Contains("import Root.Dst.Widget;",
            host.Libraries.GetModelById("Root.Src.OnlyImports")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task Move_LeavesANameAnImportBringsIn_AsWritten_InAnEncapsulatedClassToo()
    {
        // `import Root.Src.Widget; Widget w;` - the import clause names the moved class and is
        // re-qualified with it, so `Widget w` still means it. Written in full instead, it was refused
        // in every encapsulated class that imports what it uses: the usual style.
        using var host = new TestHost();
        var edit = await LoadFullNames(host, """
            within;
            package Root "root"
              package Src
                model Widget
                end Widget;
                encapsulated model Named
                  import Root.Src.Widget;
                  import W = Root.Src.Widget;
                  Widget w;
                  W v;
                end Named;
              end Src;
              package Dst
              end Dst;
            end Root;
            """);

        ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.Widget", "Root.Dst"));

        var named = host.Libraries.GetModelById("Root.Src.Named")!.Definition.ModelicaCode;
        Assert.Contains("import Root.Dst.Widget;", named);
        Assert.Contains("import W = Root.Dst.Widget;", named);
        Assert.Contains("Widget w;", named);
        Assert.Contains("W v;", named);
    }

    [Fact]
    public async Task Move_RewritesAnInheritedNameWhoseClassLeavesTheBase()
    {
        // Plant extends PartialPlant and writes `Params p`. Moving PartialPlant.Params out of the base
        // leaves the extends clause where it was, so `Params` no longer means it - it is re-qualified.
        // Left as written (it was reached through inheritance), it named a class that no longer existed.
        using var host = new TestHost();
        var edit = await LoadFullNames(host, """
            within;
            package Root "root"
              package Src
                partial model PartialPlant
                  record Params
                  end Params;
                end PartialPlant;
                model Plant
                  extends PartialPlant;
                  Params p;
                end Plant;
              end Src;
              package Dst
              end Dst;
            end Root;
            """);

        ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.PartialPlant.Params", "Root.Dst"));

        Assert.Contains("Root.Dst.Params p;", host.Libraries.GetModelById("Root.Src.Plant")!.Definition.ModelicaCode);
    }

    private static async Task<EditTools> LoadInherited(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = InheritedPackage });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        await new DependencyTools(h.Libraries, h.Impact, h.Resources, h.Session).AnalyzeDependencies();
        return new EditTools(h.Libraries, h.Resources, h.Session);
    }

    [Fact]
    public async Task Move_LeavesANameReachedThroughInheritanceAsWritten()
    {
        // Medium.State means whatever Medium the instance has. Rewritten as the base's full name it
        // would be fixed to the base's default, undoing every `redeclare package Medium = ...`, and
        // still compile. The extends clause is re-qualified, so the name resolves as written.
        using var host = new TestHost();
        var edit = await LoadInherited(host);

        ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.Base", "Root.Dst"));

        var derived = host.Libraries.GetModelById("Root.Src.Derived")!.Definition.ModelicaCode;
        Assert.Contains("extends Root.Dst.Base;", derived);
        Assert.Contains("Medium.State s;", derived);
        Assert.DoesNotContain("Base.Medium.State", derived);

        // A reference written in full is not reached through inheritance, and is re-qualified.
        Assert.Contains("Root.Dst.Base.Medium.State t;",
            host.Libraries.GetModelById("Root.Src.Other")!.Definition.ModelicaCode);
    }

    private static async Task<EditTools> LoadAndAnalyze(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(h.Libraries, h.Impact, h.Resources, h.Session);
        await deps.AnalyzeDependencies();
        return new EditTools(h.Libraries, h.Resources, h.Session);
    }

    [Fact]
    public async Task Move_RelocatesClass_AndRequalifiesReferences()
    {
        using var host = new TestHost();
        var edit = await LoadAndAnalyze(host);

        var res = ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.Widget", "Root.Dst"));
        Assert.True(res.Moved);
        Assert.Equal("Root.Dst.Widget", res.NewClassId);

        Assert.Null(host.Libraries.GetModelById("Root.Src.Widget"));
        Assert.NotNull(host.Libraries.GetModelById("Root.Dst.Widget"));

        // The external reference in UsesWidget was re-qualified to the new location.
        Assert.Contains("Root.Dst.Widget", host.Libraries.GetModelById("Root.Src.UsesWidget")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task Move_ReportsBrokenSiblingReference()
    {
        using var host = new TestHost();
        var edit = await LoadAndAnalyze(host);

        // Widget references its former sibling 'Sibling', which is not in scope under Root.Dst.
        var res = ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.Widget", "Root.Dst"));
        Assert.Contains("Sibling", res.BrokenReferencesInMovedClass);
    }

    [Fact]
    public async Task Move_Validation()
    {
        using var host = new TestHost();
        var edit = await LoadAndAnalyze(host);

        // Into itself / a descendant.
        Assert.IsType<ToolError>(await edit.MoveClass("Root.Src", "Root.Src.Widget"));
        // Non-existent destination.
        Assert.IsType<ToolError>(await edit.MoveClass("Root.Src.Widget", "Root.Nope"));
        // Already a child of that parent.
        Assert.IsType<ToolError>(await edit.MoveClass("Root.Src.Widget", "Root.Src"));
    }

    [Fact]
    public async Task Move_Collision_Rejected()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage Root\n  package A\n    model W\n Real x; end W;\n  end A;\n  package B\n    model W\n Real y; end W;\n  end B;\nend Root;"
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();

        var err = ToolAssert.Error(await new EditTools(host.Libraries, host.Resources, host.Session)
            .MoveClass("Root.A.W", "Root.B"));
        Assert.Contains("already exists", err.Error);
    }

    [Fact]
    public async Task Move_RequiresAnalysis()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var err = ToolAssert.Error(await new EditTools(host.Libraries, host.Resources, host.Session)
            .MoveClass("Root.Src.Widget", "Root.Dst"));
        Assert.Contains("mlqt_analyze_dependencies", err.Error);
    }

    [Fact]
    public async Task Move_DirectoryPackage_RelocatesFolderAndRequalifies()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            // Root.A.Sub is a directory package (Root/A/Sub/). Root.B is an empty directory package.
            // Root.User references A.Sub.Widget. Move Root.A.Sub -> Root.B.
            ["package.mo"] = "within;\npackage Root\n  model User\n    Root.A.Sub.Widget w;\n  end User;\nend Root;",
            ["package.order"] = "A\nB\nUser\n",
            ["A/package.mo"] = "within Root;\npackage A\nend A;",
            ["A/package.order"] = "Sub\n",
            ["A/Sub/package.mo"] = "within Root.A;\npackage Sub\n  model Widget\n    Real x;\n  end Widget;\nend Sub;",
            ["A/Sub/package.order"] = "Widget\n",
            ["B/package.mo"] = "within Root;\npackage B\nend B;"
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();

        var res = ToolAssert.Ok<MoveClassResult>(
            await new EditTools(host.Libraries, host.Resources, host.Session).MoveClass("Root.A.Sub", "Root.B"));
        Assert.True(res.Moved);
        Assert.Equal("Root.B.Sub", res.NewClassId);

        Assert.False(Directory.Exists(Path.Combine(dir, "A", "Sub")));
        Assert.True(Directory.Exists(Path.Combine(dir, "B", "Sub")));
        Assert.Null(host.Libraries.GetModelById("Root.A.Sub.Widget"));
        Assert.NotNull(host.Libraries.GetModelById("Root.B.Sub.Widget"));
        Assert.Contains("Root.B.Sub.Widget", host.Libraries.GetModelById("Root.User")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task Move_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var edit = await LoadAndAnalyze(host);

        var res = ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.Widget", "Root.Dst", preview: true));
        Assert.True(res.PreviewOnly);
        Assert.NotNull(host.Libraries.GetModelById("Root.Src.Widget"));
        Assert.Null(host.Libraries.GetModelById("Root.Dst.Widget"));
    }
}
