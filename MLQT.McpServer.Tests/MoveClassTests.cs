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
        Assert.Contains("analyze_dependencies", err.Error);
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
