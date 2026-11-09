using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// mlqt_format lays a class out as its repository's settings say - the layout mlqt_check judges it by -
/// unless the caller overrides an option. The options defaulted to off, so in a repository that orders
/// its sections, imports and declarations every call had to name four options, and a class formatted
/// without them failed the layout rules straight after.
/// </summary>
public class FormatDefaultsTests
{
    // Two equation sections, and a variable before the parameter it uses.
    private const string Jumbled =
        "within P;\nmodel Foo \"d\"\n  Real x;\n  parameter Real p = 1;\nequation\n  x = p;\nequation\nend Foo;\n";

    private static string Load(TestHost host, bool repositoryLayout)
    {
        var root = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["P/package.mo"] = "within;\npackage P \"p\"\nend P;\n",
            ["P/Foo.mo"] = Jumbled,
            ["P/package.order"] = "Foo\n",
        });
        var repository = host.Repositories.AddRepositoryAsync(root, startMonitoring: false)
            .GetAwaiter().GetResult().Repository!;
        host.Repositories.LoadLibrariesAsync(repository.Id).GetAwaiter().GetResult();
        var settings = repository.StyleSettings!;
        settings.OneOfEachSection = repositoryLayout;
        settings.ImportStatementsFirst = repositoryLayout;
        settings.ComponentsBeforeClasses = repositoryLayout;
        settings.DeclarationOrder = repositoryLayout;
        return Path.Combine(root, "P", "Foo.mo");
    }

    private static WriteFormatTools Tools(TestHost host) =>
        new(host.Libraries, host.Repositories, host.Resources, host.Session);

    private static int Count(string text, string what) =>
        System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(what)).Count;

    private static void AssertRepositoryLayout(string text)
    {
        Assert.Equal(1, Count(text, "equation"));                                   // one of each section
        Assert.True(text.IndexOf("parameter Real p", StringComparison.Ordinal)
                    < text.IndexOf("Real x;", StringComparison.Ordinal));          // declaration order
    }

    [Fact]
    public async Task AClass_IsFormattedWithItsRepositorysLayout_WhenNoOptionIsGiven()
    {
        using var host = new TestHost();
        var file = Load(host, repositoryLayout: true);

        ToolAssert.Ok<FormatClassResult>(await Tools(host).Format(classId: "P.Foo"));

        AssertRepositoryLayout(File.ReadAllText(file));
    }

    [Fact]
    public async Task ALibrary_IsFormattedWithItsRepositorysLayout_WhenNoOptionIsGiven()
    {
        using var host = new TestHost();
        var file = Load(host, repositoryLayout: true);

        ToolAssert.Ok<LibraryFormatResult>(await Tools(host).Format(library: "P"));

        AssertRepositoryLayout(File.ReadAllText(file));
    }

    [Fact]
    public async Task AnOptionGiven_OverridesTheRepositorysSetting()
    {
        using var host = new TestHost();
        var file = Load(host, repositoryLayout: true);

        ToolAssert.Ok<FormatClassResult>(await Tools(host).Format(classId: "P.Foo", declarationOrder: false));

        var text = File.ReadAllText(file);
        Assert.Equal(1, Count(text, "equation"));                                   // the rest still the repository's
        Assert.True(text.IndexOf("Real x;", StringComparison.Ordinal)
                    < text.IndexOf("parameter Real p", StringComparison.Ordinal));  // this one as asked
    }

    [Fact]
    public async Task ARepositoryThatAsksForNoReordering_IsNotReordered()
    {
        using var host = new TestHost();
        var file = Load(host, repositoryLayout: false);

        ToolAssert.Ok<FormatClassResult>(await Tools(host).Format(classId: "P.Foo"));

        Assert.Equal(2, Count(File.ReadAllText(file), "equation"));
    }

    [Fact]
    public async Task TextWithNoRepository_TakesOnlyTheOptionsGiven()
    {
        using var host = new TestHost();
        Load(host, repositoryLayout: true);

        var plain = ToolAssert.Ok<FormatCodeResult>(await Tools(host).Format(source: Jumbled));
        var merged = ToolAssert.Ok<FormatCodeResult>(await Tools(host).Format(source: Jumbled, oneOfEachSection: true));

        Assert.Equal(2, Count(plain.Source, "equation"));
        Assert.Equal(1, Count(merged.Source, "equation"));
    }
}
