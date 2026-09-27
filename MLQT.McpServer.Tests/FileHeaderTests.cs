using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// B445 — the MCP writers rebuild a file from its owner's stored source, which is the class's span
/// and nothing else, so a licence header above <c>within</c> (or a comment after the class) was
/// deleted by one description edit. The header stays with the class that heads the file: an edit
/// keeps it, a rename keeps it, and a move takes it along.
/// </summary>
public class FileHeaderTests
{
    private const string PackageMo = "// Package header\nwithin;\npackage Lib \"lib\"\n  package Nested\n    constant Real n = 1;\n  end Nested;\nend Lib;\n";
    private const string ModelMo = "// Copyright header\n// Licensed to everyone\n\nwithin Lib;\nmodel M \"m\"\n  Real x;\nend M;\n";

    private static string Load(TestHost host, bool withDestination = false)
    {
        var files = new Dictionary<string, string>
        {
            ["package.mo"] = PackageMo,
            ["package.order"] = withDestination ? "Nested\nM\nDst\n" : "Nested\nM\n",
            ["M.mo"] = ModelMo,
        };
        if (withDestination)
        {
            files["Dst/package.mo"] = "within Lib;\npackage Dst\nend Dst;\n";
            files["Dst/package.order"] = "";
        }

        var dir = host.WriteLibraryDir(files);
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return dir;
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static EditTools Edit(TestHost h) => new(h.Libraries, h.Resources, h.Session);

    [Fact]
    public async Task SetClassDescription_OnAFileOwner_KeepsItsHeader()
    {
        using var host = new TestHost();
        var dir = Load(host);
        var tools = new DocumentationTools(host.Libraries, host.Resources, host.Session);

        ToolAssert.Ok<StructureEditResult>(await tools.SetClassDescription("Lib.M", "new"));

        Assert.StartsWith("// Copyright header\n// Licensed to everyone\n\nwithin Lib;\nmodel M \"new\"", Read(Path.Combine(dir, "M.mo")));
        Assert.DoesNotContain("Copyright", host.Libraries.GetModelById("Lib.M")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task SetClassDescription_OnANestedClass_KeepsThePackageFilesHeader()
    {
        using var host = new TestHost();
        var dir = Load(host);
        var tools = new DocumentationTools(host.Libraries, host.Resources, host.Session);

        ToolAssert.Ok<StructureEditResult>(await tools.SetClassDescription("Lib.Nested", "described"));

        Assert.StartsWith("// Package header\nwithin;\npackage Lib", Read(Path.Combine(dir, "package.mo")));
    }

    [Fact]
    public async Task UpdateClassSource_KeepsTheHeader()
    {
        using var host = new TestHost();
        var dir = Load(host);

        ToolAssert.Ok<UpdateClassSourceResult>(
            await Edit(host).UpdateClassSource("Lib.M", "model M \"m\"\n  Real y;\nend M;"));

        Assert.StartsWith("// Copyright header\n// Licensed to everyone\n\nwithin Lib;\nmodel M", Read(Path.Combine(dir, "M.mo")));
    }

    [Fact]
    public async Task CreateAndDeleteANestedClass_KeepThePackageFilesHeader()
    {
        using var host = new TestHost();
        var dir = Load(host);
        var edit = Edit(host);

        ToolAssert.Ok<CreateClassResult>(await edit.CreateClass("Lib", "model Added\nend Added;", standalone: false));
        Assert.StartsWith("// Package header\nwithin;\n", Read(Path.Combine(dir, "package.mo")));

        ToolAssert.Ok<DeleteClassResult>(await edit.DeleteClass("Lib.Nested"));
        Assert.StartsWith("// Package header\nwithin;\n", Read(Path.Combine(dir, "package.mo")));
    }

    [Fact]
    public async Task RenameClass_KeepsTheHeader()
    {
        using var host = new TestHost();
        var dir = Load(host);
        await new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session).AnalyzeDependencies();

        ToolAssert.Ok<RenameClassResult>(await Edit(host).RenameClass("Lib.M", "Renamed"));

        var text = string.Join("\n", Directory.GetFiles(dir, "*.mo").Select(Read));
        Assert.Contains("// Copyright header\n// Licensed to everyone\n\nwithin Lib;\nmodel Renamed", text);
    }

    [Fact]
    public async Task MoveClass_TakesTheHeaderAlong()
    {
        using var host = new TestHost();
        var dir = Load(host, withDestination: true);
        await new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session).AnalyzeDependencies();

        ToolAssert.Ok<MoveClassResult>(await Edit(host).MoveClass("Lib.M", "Lib.Dst"));

        Assert.StartsWith("// Copyright header\n// Licensed to everyone\n\nwithin Lib.Dst;\nmodel M", Read(Path.Combine(dir, "Dst", "M.mo")));
    }

    [Fact]
    public async Task MoveClass_IntoAnotherClassesFile_TakesTheHeaderAlongAboveTheClass()
    {
        // Nested into a file another class heads, the header cannot head that file - it has its own -
        // so it goes with the class, directly above it.
        using var host = new TestHost();
        var dir = Load(host, withDestination: true);
        await new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session).AnalyzeDependencies();

        ToolAssert.Ok<MoveClassResult>(await Edit(host).MoveClass("Lib.M", "Lib.Nested"));

        var package = Read(Path.Combine(dir, "package.mo"));
        Assert.StartsWith("// Package header\nwithin;\n", package);
        Assert.Contains("// Copyright header\n// Licensed to everyone\nmodel M", package.Replace("\n    ", "\n").Replace("\n  ", "\n"));
    }
}
