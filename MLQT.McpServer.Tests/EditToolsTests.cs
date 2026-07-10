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

    // Loads the P package (Base <- Middle) and runs dependency analysis.
    private static async Task<(EditTools edit, DependencyTools deps)> LoadAndAnalyze(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = DepPackage });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        await deps.AnalyzeDependencies();
        return (new EditTools(host.Libraries, host.Resources, host.Session), deps);
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
