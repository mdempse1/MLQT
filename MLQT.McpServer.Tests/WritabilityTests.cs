using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Phase 0 — filesystem-inferred writability. Editing tools must refuse to write files this process
/// cannot own (e.g. a read-only reference library), and get_class_info surfaces the flag.
/// </summary>
public class WritabilityTests
{
    private const string Foo = "model Foo\n  Real x;\nequation\n  x = 1;\nend Foo;";

    private static EditTools Edit(TestHost h) => new(h.Libraries, h.Resources, h.Session);

    [Fact]
    public void IsWritable_TrueForNormalFile_FalseForReadOnly()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Foo.mo", Foo);
        Assert.True(FileWritability.IsWritable(path));

        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.False(FileWritability.IsWritable(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal); // let TestHost clean up
        }
    }

    [Fact]
    public async Task UpdateClassSource_ReadOnlyFile_Rejected_NothingWritten()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Foo.mo", Foo);
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var err = ToolAssert.Error(await Edit(host).UpdateClassSource(
                "Foo", "model Foo \"changed\"\n  Real x;\nequation\n  x = 1;\nend Foo;"));
            Assert.Contains("read-only", err.Error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("changed", File.ReadAllText(path)); // untouched on disk
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task UpdateClassSource_ReadOnlyFile_PreviewStillWorks()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Foo.mo", Foo);
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            // Preview does not write, so a read-only file must not block it.
            var res = ToolAssert.Ok<UpdateClassSourceResult>(await Edit(host).UpdateClassSource(
                "Foo", "model Foo \"changed\"\n  Real x;\nequation\n  x = 1;\nend Foo;", preview: true));
            Assert.True(res.PreviewOnly);
            Assert.Contains("changed", res.NewFileContent!);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void GetClassInfo_ReportsWritable()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Foo.mo", Foo);
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var query = new ClassQueryTools(host.Libraries);

        var info = ToolAssert.Ok<ClassInfo>(query.GetClassInfo("Foo"));
        Assert.True(info.Writable);

        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var ro = ToolAssert.Ok<ClassInfo>(query.GetClassInfo("Foo"));
            Assert.False(ro.Writable);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
