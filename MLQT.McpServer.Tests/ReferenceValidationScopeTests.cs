using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// mlqt_check's references check over more than one class: a package and everything inside it, or a
/// whole library, in one paged call. The evaluation harness found agents calling the one-class form
/// five to eight times to find a single misspelt type in an eight-class library.
/// </summary>
public class ReferenceValidationScopeTests
{
    // Three files, so the file lines reported can be told apart from the class-relative ones, and a
    // sub-package, so a package scope can be told apart from the library's.
    private static readonly Dictionary<string, string> Library = new()
    {
        ["package.mo"] = """
            within;
            package L "l"
              model Top "top"
                Real x;
                L.Sub.A a;
                Nowhere.Top t;
              end Top;
            end L;
            """,
        ["Sub/package.mo"] = """
            within L;
            package Sub "sub"
            end Sub;
            """,
        ["Sub/package.order"] = """
            A
            B
            """,
        ["Sub/A.mo"] = """
            within L.Sub;
            model A "a"
              Real x;
              Missing.Type m;
            end A;
            """,
        ["Sub/B.mo"] = """
            within L.Sub;
            model B "b"
              extends Missing.Base;
              A a;
              Also.Missing m;
            end B;
            """,
    };

    private static ViewTools Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(Library);
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new ViewTools(host.Libraries);
    }

    [Fact]
    public void Library_ReportsEveryUnresolvedReference_InOneCall()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = ToolAssert.Ok<ReferenceValidationResult>(tools.ValidateClassReferences(libraryId: "L"));

        Assert.Equal("L", res.Id);
        Assert.Equal(5, res.ClassesChecked); // L, L.Top, L.Sub, L.Sub.A, L.Sub.B
        Assert.Equal(4, res.UnresolvedCount);
        Assert.Equal(4, res.Count);
        Assert.Null(res.Unreadable);
        // Ordered by class, then as written.
        Assert.Equal(
            ["L.Sub.A:Missing.Type", "L.Sub.B:Missing.Base", "L.Sub.B:Also.Missing", "L.Top:Nowhere.Top"],
            res.Unresolved.Select(u => $"{u.ClassId}:{u.Name}"));
        // What resolves is not reported: L.Sub.A by its full name, A by package-relative lookup.
        Assert.DoesNotContain(res.Unresolved, u => u.Name is "L.Sub.A" or "A");
    }

    [Fact]
    public void Library_EachReferenceSaysWhereItIsOnDisk()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = ToolAssert.Ok<ReferenceValidationResult>(tools.ValidateClassReferences(libraryId: "L"));

        var inA = res.Unresolved.Single(u => u.Name == "Missing.Type");
        Assert.Equal("component-type", inA.Kind);
        Assert.Equal("A.mo", Path.GetFileName(inA.FilePath));
        Assert.Equal(4, inA.FileLine); // below the within clause, which the class's own source has not got
        Assert.Equal(3, inA.Line);

        var inTop = res.Unresolved.Single(u => u.Name == "Nowhere.Top");
        Assert.Equal("package.mo", Path.GetFileName(inTop.FilePath));
        Assert.Equal(6, inTop.FileLine);

        Assert.Equal("extends", res.Unresolved.Single(u => u.Name == "Missing.Base").Kind);
    }

    [Fact]
    public void Package_CoversEverythingInside_AndNothingBeside()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = ToolAssert.Ok<ReferenceValidationResult>(tools.ValidateClassReferences("L.Sub"));

        Assert.Equal("L.Sub", res.Id);
        Assert.Equal(3, res.ClassesChecked);
        Assert.Equal(["L.Sub.A", "L.Sub.B", "L.Sub.B"], res.Unresolved.Select(u => u.ClassId));
    }

    [Fact]
    public void OneClass_IsStillJustThatClass()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = ToolAssert.Ok<ReferenceValidationResult>(tools.ValidateClassReferences("L.Sub.B"));

        Assert.Equal("L.Sub.B", res.Id);
        Assert.Equal(1, res.ClassesChecked);
        Assert.Equal(3, res.Checked);
        Assert.Equal(["Missing.Base", "Also.Missing"], res.Unresolved.Select(u => u.Name));
        Assert.All(res.Unresolved, u => Assert.Equal("L.Sub.B", u.ClassId));
    }

    [Fact]
    public void Pages_WithLimitAndOffset_CountingTheWhole()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var page = ToolAssert.Ok<ReferenceValidationResult>(
            tools.ValidateClassReferences(libraryId: "L", limit: 2, offset: 1));

        Assert.Equal(4, page.UnresolvedCount);
        Assert.Equal(1, page.Offset);
        Assert.Equal(2, page.Count);
        Assert.Equal(["Missing.Base", "Also.Missing"], page.Unresolved.Select(u => u.Name));

        var past = ToolAssert.Ok<ReferenceValidationResult>(
            tools.ValidateClassReferences(libraryId: "L", offset: 10));
        Assert.Equal(4, past.UnresolvedCount);
        Assert.Empty(past.Unresolved);
    }

    [Fact]
    public void LimitIsClamped_NotRefused()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var none = ToolAssert.Ok<ReferenceValidationResult>(
            tools.ValidateClassReferences(libraryId: "L", limit: 0, offset: -3));
        Assert.Equal(0, none.Offset);
        Assert.Single(none.Unresolved);

        var huge = ToolAssert.Ok<ReferenceValidationResult>(
            tools.ValidateClassReferences(libraryId: "L", limit: ViewTools.MaxReferenceLimit + 1));
        Assert.Equal(4, huge.Count);
    }

    [Fact]
    public void BothOrNeitherScope_IsAnError()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var both = Assert.IsType<ToolError>(tools.ValidateClassReferences("L.Top", libraryId: "L"));
        Assert.Contains("not both", both.Error);
        var neither = Assert.IsType<ToolError>(tools.ValidateClassReferences());
        Assert.Contains("library_id", neither.Error);
    }

    [Fact]
    public void UnknownLibrary_IsAnError()
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.IsType<ToolError>(tools.ValidateClassReferences(libraryId: "NoSuchLibrary"));
    }

    [Fact]
    public void NoLibraryLoaded_IsAnError()
    {
        using var host = new TestHost();

        Assert.IsType<ToolError>(new ViewTools(host.Libraries).ValidateClassReferences(libraryId: "L"));
    }

    [Fact]
    public void AClassThatWillNotParse_IsNamed_NotSkippedSilently()
    {
        using var host = new TestHost();
        var files = new Dictionary<string, string>(Library)
        {
            ["Sub/package.order"] = "A\nB\nBroken\n",
            ["Sub/Broken.mo"] = "within L.Sub;\nmodel Broken (((\n",
        };
        var dir = host.WriteLibraryDir(files);
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var res = ToolAssert.Ok<ReferenceValidationResult>(
            new ViewTools(host.Libraries).ValidateClassReferences(libraryId: "L"));

        Assert.NotNull(res.Unreadable);
        Assert.Contains(res.Unreadable!, id => id.Contains("Broken", StringComparison.Ordinal));
        Assert.Equal(4, res.UnresolvedCount); // the rest is still checked
    }
}
