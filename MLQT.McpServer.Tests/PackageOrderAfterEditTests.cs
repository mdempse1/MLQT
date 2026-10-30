using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using ModelicaGraph.DataTypes;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A class written, deleted or renamed through the edit tools is listed in <c>package.order</c> on disk,
/// and the next check reads that order: it does not report the new class missing from it, or the old one
/// listed in it, until the library is loaded again.
/// </summary>
public class PackageOrderAfterEditTests
{
    private static (EditTools Edit, StyleTools Style) Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage P \"p\"\nend P;\n",
            ["A.mo"] = "within P;\nmodel A \"a\"\nend A;\n",
            ["package.order"] = "A\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return (new EditTools(host.Libraries, host.Resources, host.Session),
            new StyleTools(host.Libraries, host.CodeReview, host.Repositories, host.CustomDictionary,
                host.DictionaryManager, host.Session));
    }

    private static async Task<List<StyleFindingDto>> PackageOrderFindings(StyleTools style) =>
        ToolAssert.Ok<CheckResult>(await style.CheckLibrary(settings: new StyleSettingsInput { CheckPackageOrder = true }))
            .Findings.Where(f => f.RuleId == ModelicaParser.StyleRules.RuleIds.PackageOrder).ToList();

    private static void Succeeded(object result)
    {
        if (result is ToolError error)
            throw new Xunit.Sdk.XunitException($"The edit failed: {error.Error}");
    }

    private static string[] OrderOf(TestHost host) =>
        host.Libraries.GetModelById("P")!.PackageOrder!.Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

    [Fact]
    public async Task AClassWrittenToItsOwnFile_IsInTheOrderTheCheckReads()
    {
        using var host = new TestHost();
        var (edit, style) = Load(host);

        Succeeded(await edit.CreateClass("P", "model B \"b\"\nend B;", standalone: true));

        Assert.Equal(["A", "B"], OrderOf(host));
        Assert.Empty(await PackageOrderFindings(style));
    }

    [Fact]
    public async Task AClassDeleted_IsGoneFromTheOrderTheCheckReads()
    {
        using var host = new TestHost();
        var (edit, style) = Load(host);
        Succeeded(await edit.CreateClass("P", "model B \"b\"\nend B;", standalone: true));

        Succeeded(await edit.DeleteClass("P.A"));

        Assert.Equal(["B"], OrderOf(host));
        Assert.Empty(await PackageOrderFindings(style));
    }

    [Fact]
    public async Task AClassRenamed_IsRenamedInTheOrderTheCheckReads()
    {
        using var host = new TestHost();
        var (edit, style) = Load(host);
        await new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session).AnalyzeDependencies();

        Succeeded(await edit.RenameClass("P.A", "Renamed"));

        Assert.Equal(["Renamed"], OrderOf(host));
        Assert.Empty(await PackageOrderFindings(style));
    }

    [Fact]
    public async Task AClassWrittenIntoThePackageFile_KeepsThePackagesOrder()
    {
        // Re-parsing package.mo makes a new node for P; it used to come back with no order at all, so
        // the check said nothing about P's package.order and a later format worked one out.
        using var host = new TestHost();
        var (edit, _) = Load(host);

        Succeeded(await edit.CreateClass("P", "model C \"c\"\nend C;", standalone: false));

        Assert.Contains("A", OrderOf(host));
    }
}
