using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class EditToolsTests
{
    private const string Package = """
        within;
        package P "p"
          model A "a1"
            Real a;
          equation
            a = 1;
          end A;

          model B "b1"
            Real b;
          equation
            b = 2;
          end B;
        end P;
        """;

    private const string DepPackage = """
        within;
        package P "p"
          model Base "b"
            Real x;
          equation
            x = time;
          end Base;

          model Middle "m"
            Base base1 "a base";
          end Middle;
        end P;
        """;

    private static EditTools Edit(TestHost h) => new(h.Libraries, h.Resources, h.Session);

    private static void LoadFile(TestHost h, string name, string content)
        => h.Libraries.AddLibraryFromFileAsync(h.WriteMoFile(name, content)).GetAwaiter().GetResult();

    // Two 'Widget' classes in sibling packages, each used by a local model — for precision tests.
    private const string SameLeafPackage = """
        within;
        package Q "q"
          package A
            model Widget "a widget"
              Real x;
            end Widget;
            model UserA
              Widget w;
            end UserA;
          end A;
          package B
            model Widget "b widget"
              Real y;
            end Widget;
            model UserB
              Widget w;
            end UserB;
          end B;
        end Q;
        """;

    // Loads the P package (Base <- Middle) and runs dependency analysis.
    private static async Task<(EditTools edit, DependencyTools deps)> LoadAndAnalyze(TestHost host)
        => await LoadDirAndAnalyze(host, DepPackage);

    private static async Task<(EditTools edit, DependencyTools deps)> LoadDirAndAnalyze(TestHost host, string packageContent)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = packageContent });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();
        return (new EditTools(host.Libraries, host.Resources, host.Session), deps);
    }

    [Fact]
    public async Task RenameClass_UpdatesDeclarationReferencesAndDependencies()
    {
        using var host = new TestHost();
        var (edit, deps) = await LoadAndAnalyze(host);

        var res = ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("P.Base", "NewBase"));
        Assert.True(res.Changed);
        Assert.Equal("P.NewBase", res.NewClassId);

        Assert.Null(host.Libraries.GetModelById("P.Base"));
        Assert.NotNull(host.Libraries.GetModelById("P.NewBase"));

        // The reference inside Middle was rewritten.
        Assert.Contains("NewBase base1", host.Libraries.GetModelById("P.Middle")!.Definition.ModelicaCode);

        // Dependency graph refreshed.
        var used = ToolAssert.Ok<DependencyResult>(deps.GetDependencies("P.Middle"));
        Assert.Contains(used.Items, i => i.Id == "P.NewBase");
        Assert.DoesNotContain(used.Items, i => i.Id == "P.Base");
    }

    [Fact]
    public async Task RenameClass_DoesNotTouchSameNamedUnrelatedClass()
    {
        using var host = new TestHost();
        var (edit, _) = await LoadDirAndAnalyze(host, SameLeafPackage);

        // Rename only Q.A.Widget -> Gadget. Q.B.Widget and B's usage must be untouched.
        var res = ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Q.A.Widget", "Gadget"));
        Assert.True(res.Changed);

        Assert.NotNull(host.Libraries.GetModelById("Q.A.Gadget"));
        Assert.Null(host.Libraries.GetModelById("Q.A.Widget"));
        Assert.NotNull(host.Libraries.GetModelById("Q.B.Widget")); // sibling untouched

        Assert.Contains("Gadget w", host.Libraries.GetModelById("Q.A.UserA")!.Definition.ModelicaCode);
        Assert.Contains("Widget w", host.Libraries.GetModelById("Q.B.UserB")!.Definition.ModelicaCode);
        Assert.DoesNotContain("Gadget", host.Libraries.GetModelById("Q.B.UserB")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task RenameClass_DirectoryPackage_RenamesFolderAndRequalifies()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            // Root has a directory subpackage Sub (with Widget + a nested User referencing it), and a
            // top-level User2 that references Sub.Widget by qualified name.
            ["package.mo"] = "within;\npackage Root\n  model User2\n    Root.Sub.Widget w;\n  end User2;\nend Root;",
            ["package.order"] = "Sub\nUser2\n",
            ["Sub/package.mo"] = "within Root;\npackage Sub\n  model Widget\n    Real x;\n  end Widget;\n  model Local\n    Widget w;\n  end Local;\nend Sub;",
            ["Sub/package.order"] = "Widget\nLocal\n"
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();

        var res = ToolAssert.Ok<RenameClassResult>(await Edit(host).RenameClass("Root.Sub", "Components"));
        Assert.True(res.Changed);
        Assert.Equal("Root.Components", res.NewClassId);

        // Folder renamed.
        Assert.False(Directory.Exists(Path.Combine(dir, "Sub")));
        Assert.True(Directory.Exists(Path.Combine(dir, "Components")));

        // Ids remapped.
        Assert.Null(host.Libraries.GetModelById("Root.Sub.Widget"));
        Assert.NotNull(host.Libraries.GetModelById("Root.Components.Widget"));
        Assert.NotNull(host.Libraries.GetModelById("Root.Components.Local"));

        // External qualified reference re-qualified.
        Assert.Contains("Root.Components.Widget", host.Libraries.GetModelById("Root.User2")!.Definition.ModelicaCode);
        // package.order entry renamed.
        Assert.Contains("Components", File.ReadAllLines(Path.Combine(dir, "package.order")));
        Assert.DoesNotContain("Sub", File.ReadAllLines(Path.Combine(dir, "package.order")));
    }

    [Fact]
    public async Task RenameClass_ReadOnlyFile_Aborts()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = DepPackage });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();

        var path = Path.Combine(dir, "package.mo");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var err = ToolAssert.Error(await Edit(host).RenameClass("P.Base", "NewBase"));
            Assert.Contains("read-only", err.Error, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(host.Libraries.GetModelById("P.Base")); // unchanged
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task RenameClass_RequiresAnalysis()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = DepPackage });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var err = ToolAssert.Error(await Edit(host).RenameClass("P.Base", "NewBase"));
        Assert.Contains("analyze_dependencies", err.Error);
    }

    [Fact]
    public async Task RenameClass_Validation()
    {
        using var host = new TestHost();
        var (edit, _) = await LoadAndAnalyze(host);

        Assert.IsType<ToolError>(await edit.RenameClass("P.Base", "1Bad"));   // invalid identifier
        Assert.IsType<ToolError>(await edit.RenameClass("P.Base", "Middle")); // collides with P.Middle
        Assert.IsType<ToolError>(await edit.RenameClass("P.Nope", "X"));      // not found
    }

    [Fact]
    public async Task RenameClass_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var (edit, _) = await LoadAndAnalyze(host);

        var res = ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("P.Base", "NewBase", preview: true));
        Assert.True(res.PreviewOnly);
        Assert.True(res.Changes.Count >= 1);
        Assert.All(res.Changes, c => Assert.NotNull(c.NewContent));
        Assert.NotNull(host.Libraries.GetModelById("P.Base")); // not renamed on disk/graph
        Assert.Null(host.Libraries.GetModelById("P.NewBase"));
    }

    [Fact]
    public async Task UpdateClassSource_RefreshesDependencies()
    {
        using var host = new TestHost();
        var (edit, deps) = await LoadAndAnalyze(host);

        var before = ToolAssert.Ok<DependencyResult>(deps.GetDependencies("P.Middle"));
        Assert.Contains(before.Items, i => i.Id == "P.Base");

        // Middle no longer uses Base.
        await edit.UpdateClassSource("P.Middle", "model Middle \"m\"\n  Real y;\nequation\n  y = 1;\nend Middle;");

        // Dependency graph auto-refreshed without a manual analyze_dependencies re-run.
        var after = ToolAssert.Ok<DependencyResult>(deps.GetDependencies("P.Middle"));
        Assert.DoesNotContain(after.Items, i => i.Id == "P.Base");
    }

    [Fact]
    public async Task UpdateClassSource_Standalone_WritesAndReloads()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Foo.mo", "model Foo \"old\"\n  Real x;\nequation\n  x = 1;\nend Foo;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();

        var res = ToolAssert.Ok<UpdateClassSourceResult>(await Edit(host).UpdateClassSource(
            "Foo", "model Foo \"new desc\"\n  Real y;\nequation\n  y = 2;\nend Foo;"));

        Assert.True(res.Changed);
        Assert.Contains("new desc", File.ReadAllText(path));               // written to disk
        Assert.Contains("new desc", host.Libraries.GetModelById("Foo")!.Definition.ModelicaCode); // graph reloaded
    }

    [Fact]
    public async Task UpdateClassSource_NestedClass_ReplacesOnlyThatClass()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var res = ToolAssert.Ok<UpdateClassSourceResult>(await Edit(host).UpdateClassSource(
            "P.A", "model A \"A updated\"\n  Real a2;\nequation\n  a2 = 42;\nend A;"));
        Assert.True(res.Changed);

        var a = host.Libraries.GetModelById("P.A");
        Assert.Contains("A updated", a!.Definition.ModelicaCode);
        Assert.Contains("a2 = 42", a.Definition.ModelicaCode);

        var b = host.Libraries.GetModelById("P.B"); // sibling untouched
        Assert.NotNull(b);
        Assert.Contains("b1", b!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task UpdateClassSource_Rename_Rejected()
    {
        using var host = new TestHost();
        LoadFile(host, "Foo.mo", "model Foo\n  Real x;\nequation\n  x = 1;\nend Foo;");
        var edit = Edit(host);

        // Renaming via update_class_source is not supported — the class name must stay the same.
        var err = ToolAssert.Error(await edit.UpdateClassSource(
            "Foo", "model Bar\n  Real x;\nequation\n  x = 1;\nend Bar;"));
        Assert.Contains("rename", err.Error, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(host.Libraries.GetModelById("Foo")); // unchanged
        Assert.Null(host.Libraries.GetModelById("Bar"));
    }

    [Fact]
    public async Task UpdateClassSource_SyntaxError_Rejected()
    {
        using var host = new TestHost();
        LoadFile(host, "Foo.mo", "model Foo\n  Real x;\nequation\n  x = 1;\nend Foo;");
        var err = ToolAssert.Error(await Edit(host).UpdateClassSource("Foo", "model Foo\n  Real x = ;\nend Foo;"));
        Assert.Contains("syntax", err.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateClassSource_MultipleClasses_Rejected()
    {
        using var host = new TestHost();
        LoadFile(host, "Foo.mo", "model Foo\n  Real x;\nequation\n  x = 1;\nend Foo;");
        var err = ToolAssert.Error(await Edit(host).UpdateClassSource(
            "Foo", "model Foo\n  Real x;\nequation\n  x = 1;\nend Foo;\nmodel Extra Real z; end Extra;"));
        Assert.Contains("one", err.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateClassSource_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Foo.mo", "model Foo\n  Real x;\nequation\n  x = 1;\nend Foo;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var before = File.ReadAllText(path);

        var res = ToolAssert.Ok<UpdateClassSourceResult>(await Edit(host).UpdateClassSource(
            "Foo", "model Foo \"preview only\"\n  Real x;\nequation\n  x = 1;\nend Foo;", preview: true));

        Assert.True(res.PreviewOnly);
        Assert.NotNull(res.NewFileContent);
        Assert.Contains("preview only", res.NewFileContent!);
        Assert.Equal(before, File.ReadAllText(path)); // unchanged on disk
    }

    [Fact]
    public async Task UpdateClassSource_MissingClass_And_EmptySource_Error()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(await Edit(host).UpdateClassSource("Nope", "model Nope end Nope;"));

        LoadFile(host, "Foo.mo", "model Foo\n  Real x;\nequation\n  x = 1;\nend Foo;");
        Assert.IsType<ToolError>(await Edit(host).UpdateClassSource("Foo", "   "));
    }
}
