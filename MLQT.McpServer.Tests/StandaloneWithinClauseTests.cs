using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using ModelicaParser.Helpers;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The two paths that write a brand-new standalone .mo file — creating a class and moving one into a
/// directory package — build that file's within clause from the destination id. The destination is
/// what decides where the class lives, so the clause must name it, exactly once.
/// </summary>
public class StandaloneWithinClauseTests
{
    // A directory-package layout, so Root.Src and Root.Dst are real folders and the standalone
    // branches are the ones taken.
    private static string WriteLibrary(TestHost h) => h.WriteLibraryDir(new Dictionary<string, string>
    {
        ["package.mo"] = "within;\npackage Root \"root\"\nend Root;\n",
        ["package.order"] = "Src\nDst\n",
        ["Src/package.mo"] = "within Root;\npackage Src \"src\"\nend Src;\n",
        ["Src/package.order"] = "Widget\n",
        ["Src/Widget.mo"] = "within Root.Src;\nmodel Widget \"w\"\n  Real x;\nend Widget;\n",
        ["Dst/package.mo"] = "within Root;\npackage Dst \"dst\"\nend Dst;\n",
        ["Dst/package.order"] = "",
    });

    private static async Task<EditTools> Load(TestHost h)
    {
        var dir = WriteLibrary(h);
        await h.Libraries.AddLibraryFromDirectoryAsync(dir);
        var deps = new DependencyTools(h.Libraries, h.Impact, h.Resources, h.Session);
        await deps.AnalyzeDependencies();
        return new EditTools(h.Libraries, h.Resources, h.Session);
    }

    private static void AssertSingleWithin(string path, string expectedParent)
    {
        Assert.True(File.Exists(path), $"expected a file at {path}");
        var text = ModelicaFileEncoding.ReadAllTextOnly(path);
        Assert.StartsWith($"within {expectedParent};", text);

        // Exactly one clause: removing it must not reveal a second. Counting the substring would
        // not do — a member called "withinTolerance" contains it.
        Assert.False(WithinClause.Has(WithinClause.Strip(text)));

        // And the file has to parse, which a duplicated clause no longer does.
        var (_, errors) = ModelicaParserHelper.ParseWithErrors(text);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task CreateClass_WritesOneWithinClauseNamingTheParent()
    {
        using var host = new TestHost();
        var edit = await Load(host);
        var dir = host.Libraries.Libraries.Single().SourcePath;

        ToolAssert.Ok<CreateClassResult>(
            await edit.CreateClass("Root.Dst", "model Fresh \"f\"\n  Real z;\nend Fresh;"));

        AssertSingleWithin(Path.Combine(dir, "Dst", "Fresh.mo"), "Root.Dst");
        Assert.NotNull(host.Libraries.GetModelById("Root.Dst.Fresh"));
    }

    [Fact]
    public async Task CreateClass_RejectsSourceCarryingItsOwnWithinClause()
    {
        using var host = new TestHost();
        var edit = await Load(host);

        // The parent comes from parent_id, so a clause in the source is a contradiction, not input.
        ToolAssert.Error(await edit.CreateClass(
            "Root.Dst", "within Somewhere.Else;\nmodel Fresh\nend Fresh;"));
    }

    [Fact]
    public async Task CreateClass_AcceptsAClassWhoseMemberNameStartsWithTheKeyword()
    {
        using var host = new TestHost();
        var edit = await Load(host);
        var dir = host.Libraries.Libraries.Single().SourcePath;

        // "withinTolerance" begins with the keyword but is not a within clause.
        ToolAssert.Ok<CreateClassResult>(await edit.CreateClass(
            "Root.Dst", "model Fresh \"f\"\n  Real withinTolerance;\nend Fresh;"));

        AssertSingleWithin(Path.Combine(dir, "Dst", "Fresh.mo"), "Root.Dst");
    }

    [Fact]
    public async Task MoveClass_WritesOneWithinClauseNamingTheDestination()
    {
        using var host = new TestHost();
        var edit = await Load(host);
        var dir = host.Libraries.Libraries.Single().SourcePath;

        var res = ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.Widget", "Root.Dst"));
        Assert.True(res.Moved);

        AssertSingleWithin(Path.Combine(dir, "Dst", "Widget.mo"), "Root.Dst");
        Assert.False(File.Exists(Path.Combine(dir, "Src", "Widget.mo")));
        Assert.NotNull(host.Libraries.GetModelById("Root.Dst.Widget"));
    }

    [Fact]
    public async Task MoveClass_WritesTheDestinationClauseEvenIfTheStoredSourceCarriesOne()
    {
        using var host = new TestHost();
        var edit = await Load(host);
        var dir = host.Libraries.Libraries.Single().SourcePath;

        // A node's stored code is within-less by convention, but the convention has been broken
        // before. If it ever is again, the moved file must still name its new home rather than
        // keeping the old clause or gaining a second one.
        var widget = host.Libraries.GetModelById("Root.Src.Widget")!;
        widget.Definition.ModelicaCode = "within Root.Src;\n" + widget.Definition.ModelicaCode;

        ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src.Widget", "Root.Dst"));

        AssertSingleWithin(Path.Combine(dir, "Dst", "Widget.mo"), "Root.Dst");
    }
}
