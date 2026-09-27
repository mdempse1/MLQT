using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// B456 — a standalone class lives in a file of its own name (MLS 13.4), and a directory package's
/// <c>package.order</c> lists its children by name, so renaming the class has to rename both with it.
/// <c>rename_class</c> rewrote the declaration inside <c>M.mo</c> and left the file and the order
/// entry alone, so <c>model Renamed</c> sat in <c>M.mo</c> where no tool looks for it.
/// </summary>
public class RenameClassStorageTests
{
    private const string PackageMo = "within;\npackage Lib \"lib\"\n  model Nested\n    M m;\n  end Nested;\nend Lib;\n";
    private const string ModelMo = "// Copyright header\n\nwithin Lib;\nmodel M \"m\"\n  Real x;\nend M;\n";

    private static async Task<(string dir, EditTools edit)> Load(TestHost host, Dictionary<string, string>? extra = null)
    {
        var files = new Dictionary<string, string>
        {
            ["package.mo"] = PackageMo,
            ["package.order"] = "Nested\nM\n",
            ["M.mo"] = ModelMo,
        };
        foreach (var (k, v) in extra ?? new())
            files[k] = v;
        var dir = host.WriteLibraryDir(files);
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        await new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session).AnalyzeDependencies();
        return (dir, new EditTools(host.Libraries, host.Resources, host.Session));
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static string[] Order(string dir) => File.ReadAllLines(Path.Combine(dir, "package.order"));

    private static string FileOf(TestHost host, string classId)
    {
        var node = host.Libraries.GetModelById(classId)!;
        return host.Libraries.CombinedGraph.GetNode<ModelicaGraph.DataTypes.FileNode>(node.ContainingFileId!)!.FilePath;
    }

    [Fact]
    public async Task RenamingAStandaloneClass_RenamesItsFileAndItsOrderEntry()
    {
        using var host = new TestHost();
        var (dir, edit) = await Load(host);

        var res = ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Lib.M", "Renamed"));

        Assert.True(res.Changed);
        Assert.False(File.Exists(Path.Combine(dir, "M.mo")));
        var renamed = Path.Combine(dir, "Renamed.mo");
        Assert.True(File.Exists(renamed));
        // The file's header goes with it (B445), and the declaration is renamed.
        Assert.Equal("// Copyright header\n\nwithin Lib;\nmodel Renamed \"m\"\n  Real x;\nend Renamed;\n", Read(renamed));
        Assert.Equal(new[] { "Nested", "Renamed" }, Order(dir));

        // The graph knows the class by its new file, and the reference in package.mo was rewritten.
        Assert.Null(host.Libraries.GetModelById("Lib.M"));
        Assert.Equal(renamed, FileOf(host, "Lib.Renamed"), ignoreCase: true);
        Assert.Contains("Renamed m;", Read(Path.Combine(dir, "package.mo")));
        // Reported against the file it now lives in.
        Assert.Contains(res.Changes, c => string.Equals(c.FilePath, renamed, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RenamingANestedClassOfADirectoryPackage_RenamesItsOrderEntry()
    {
        // package.order lists the classes nested in package.mo as well as the standalone ones.
        using var host = new TestHost();
        var (dir, edit) = await Load(host);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Lib.Nested", "Inner"));

        Assert.Equal(new[] { "Inner", "M" }, Order(dir));
        Assert.True(File.Exists(Path.Combine(dir, "M.mo")));
        Assert.NotNull(host.Libraries.GetModelById("Lib.Inner"));
    }

    [Fact]
    public async Task RenamingAStandaloneClass_OntoAnExistingFile_IsRefusedAndChangesNothing()
    {
        using var host = new TestHost();
        var (dir, edit) = await Load(host, new() { ["Renamed.mo"] = "within Lib;\nmodel Other\nend Other;\n" });

        var err = ToolAssert.Error(await edit.RenameClass("Lib.M", "Renamed"));

        Assert.Contains("Renamed.mo", err.Error);
        Assert.Equal(ModelMo, Read(Path.Combine(dir, "M.mo")));
        Assert.Equal(PackageMo, Read(Path.Combine(dir, "package.mo")));
        Assert.Equal(new[] { "Nested", "M" }, Order(dir));
        Assert.NotNull(host.Libraries.GetModelById("Lib.M"));
    }

    [Fact]
    public async Task RenamingAStandaloneClass_InAFileNotNamedAfterIt_LeavesTheFileName()
    {
        // Only the file-per-class convention is maintained; a file that never followed it is not
        // renamed onto a name it did not have.
        using var host = new TestHost();
        var (dir, edit) = await Load(host, new()
        {
            ["Odd.mo"] = "within Lib;\nmodel Stray\nend Stray;\n",
            ["package.order"] = "Nested\nM\nStray\n",
        });

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Lib.Stray", "Straying"));

        Assert.True(File.Exists(Path.Combine(dir, "Odd.mo")));
        Assert.False(File.Exists(Path.Combine(dir, "Straying.mo")));
        Assert.Equal(new[] { "Nested", "M", "Straying" }, Order(dir));
    }

    [Fact]
    public async Task Preview_SaysTheFileWouldBeRenamed_AndWritesNothing()
    {
        using var host = new TestHost();
        var (dir, edit) = await Load(host);

        var res = ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Lib.M", "Renamed", preview: true));

        Assert.Contains("M.mo", res.Note);
        Assert.Contains("Renamed.mo", res.Note);
        Assert.True(File.Exists(Path.Combine(dir, "M.mo")));
        Assert.Equal(new[] { "Nested", "M" }, Order(dir));
    }
}
