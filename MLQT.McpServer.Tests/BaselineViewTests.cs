using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using MLQT.Services.Checking;
using ModelicaParser.StyleRules;
using Git = LibGit2Sharp;

namespace MLQT.McpServer.Tests;

/// <summary>
/// How an agent finds what its change introduced in a library carrying accepted debt: every finding of
/// <c>mlqt_check</c> and <c>mlqt_list_findings</c> says where it stands against the repository's
/// committed baseline, and <c>baseline: 'new'</c> returns only what the baseline does not hold.
/// </summary>
public class BaselineViewTests
{
    private static readonly Git.Signature Author = new("Test Author", "test@test.com", DateTimeOffset.Now);

    /// <summary>
    /// A Git repository whose three classes all lack a description. The baseline accepts that debt in
    /// <c>Kept</c> and <c>Edited</c>; <c>Edited</c> is then changed in the working copy, and <c>Added</c>
    /// is not in the baseline at all - the finding an agent's change would introduce.
    /// </summary>
    private static async Task<(StyleTools Style, string Root)> RepositoryWithBaselineAsync(TestHost host, bool writeBaseline = true)
    {
        var root = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["TestLib/package.mo"] = "package TestLib \"Test library\"\nend TestLib;\n",
            ["TestLib/package.order"] = "Kept\nEdited\nAdded\n",
            ["TestLib/Kept.mo"] = "within TestLib;\nmodel Kept\nend Kept;\n",
            ["TestLib/Edited.mo"] = "within TestLib;\nmodel Edited\nend Edited;\n",
            ["TestLib/Added.mo"] = "within TestLib;\nmodel Added\nend Added;\n",
        });
        Git.Repository.Init(root);
        using (var repo = new Git.Repository(root))
        {
            Git.Commands.Stage(repo, "*");
            repo.Commit("Initial commit", Author, Author, new Git.CommitOptions());
        }

        var added = await host.Repositories.AddRepositoryAsync(root, startMonitoring: false);
        await host.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        var style = new StyleTools(host.Libraries, host.CodeReview, host.Repositories, host.CustomDictionary,
            host.DictionaryManager, host.Session,
            new BaselineStatusService(host.Libraries, host.Repositories, host.FileMonitoring));
        await style.SetStyleSettings(new StyleSettingsInput { ClassHasDescription = true });

        if (writeBaseline)
        {
            // The baseline is what the check reports today, less Added's finding.
            Assert.Equal(3, ToolAssert.Ok<CheckResult>(await style.CheckLibrary()).FindingCount);
            new Baseline(host.CodeReview.LogMessages
                    .Where(m => m.ModelName != "TestLib.Added")
                    .Select(m => new BaselineEntry(m.Fingerprint!, m.RuleId!, m.ModelName, m.ElementPath, m.Summary))
                    .ToList())
                .Save(Path.Combine(root, ".mlqt", "baseline.json"));
        }

        File.AppendAllText(Path.Combine(root, "TestLib", "Edited.mo"), "// edited\n");
        return (style, root);
    }

    private static Dictionary<string, string?> StatusByClass(CheckResult result) =>
        result.Findings.ToDictionary(f => f.ModelName, f => f.Status);

    [Fact]
    public async Task EveryFinding_SaysWhereItStandsAgainstTheBaseline()
    {
        using var host = new TestHost();
        var (style, _) = await RepositoryWithBaselineAsync(host);

        var result = ToolAssert.Ok<CheckResult>(await style.CheckLibrary());

        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["TestLib.Kept"] = "AcceptedDebt",
                ["TestLib.Edited"] = "TouchedDebt",
                ["TestLib.Added"] = "New",
            },
            StatusByClass(result));
        Assert.All(result.Findings, f => Assert.Equal(RuleIds.ClassDescription, f.RuleId));
        Assert.Equal(new BaselineSummary(New: 1, TouchedDebt: 1, AcceptedDebt: 1, Unclassified: 0, NotShown: 0), result.Baseline);
        Assert.Equal(3, result.FindingCount);
    }

    [Fact]
    public async Task New_IsOnlyWhatTheBaselineDoesNotHold()
    {
        using var host = new TestHost();
        var (style, _) = await RepositoryWithBaselineAsync(host);

        var result = ToolAssert.Ok<CheckResult>(await style.CheckLibrary(baseline: "new"));

        Assert.Equal("TestLib.Added", Assert.Single(result.Findings).ModelName);
        Assert.Equal(1, result.FindingCount);
        Assert.Equal(2, result.Baseline!.NotShown);
    }

    [Fact]
    public async Task Changed_AddsTheDebtInClassesTheWorkingCopyChanged()
    {
        using var host = new TestHost();
        var (style, _) = await RepositoryWithBaselineAsync(host);

        var result = ToolAssert.Ok<CheckResult>(await style.CheckLibrary(baseline: "changed"));

        Assert.Equal(["TestLib.Added", "TestLib.Edited"], result.Findings.Select(f => f.ModelName).Order());
    }

    [Fact]
    public async Task OneClass_IsClassifiedAsTheLibraryIs()
    {
        // The baseline was taken from a library check; a check of one class must match it.
        using var host = new TestHost();
        var (style, _) = await RepositoryWithBaselineAsync(host);

        Assert.Empty(ToolAssert.Ok<CheckResult>(style.CheckClass("TestLib.Edited", baseline: "new")).Findings);
        Assert.Equal("TouchedDebt",
            Assert.Single(ToolAssert.Ok<CheckResult>(style.CheckClass("TestLib.Edited", baseline: "changed")).Findings).Status);
        Assert.Equal("New",
            Assert.Single(ToolAssert.Ok<CheckResult>(style.CheckClass("TestLib.Added", baseline: "new")).Findings).Status);
    }

    [Fact]
    public async Task ListFindings_TakesTheSameView()
    {
        using var host = new TestHost();
        var (style, _) = await RepositoryWithBaselineAsync(host);
        await style.CheckLibrary();

        var all = ToolAssert.Ok<FindingsResult>(style.ListFindings());
        var onlyNew = ToolAssert.Ok<FindingsResult>(style.ListFindings(baseline: "new"));

        Assert.Equal(3, all.Total);
        var item = Assert.Single(onlyNew.Items);
        Assert.Equal(("TestLib.Added", "New", RuleIds.ClassDescription), (item.ModelId, item.Status, item.RuleId));
    }

    [Fact]
    public async Task WithNoBaseline_EveryViewShowsEveryFinding_AndNoneHasAStatus()
    {
        // "Cannot be classified" is not "already accepted", so nothing is hidden.
        using var host = new TestHost();
        var (style, _) = await RepositoryWithBaselineAsync(host, writeBaseline: false);

        var result = ToolAssert.Ok<CheckResult>(await style.CheckLibrary(baseline: "new"));

        Assert.Equal(3, result.FindingCount);
        Assert.All(result.Findings, f => Assert.Null(f.Status));
        Assert.Null(result.Baseline);
    }

    [Fact]
    public async Task TheServer_GivesTheToolsTheClassification()
    {
        // The tools take the baseline service as an optional parameter; through the server's own
        // registrations it must arrive, or every finding would silently go unclassified. An empty
        // committed baseline accepts nothing, so each finding is New.
        using var host = new TestHost();   // its clean-up survives Git's read-only object files
        await using var server = await InProcessServer.StartAsync();
        var root = host.NewTempDir();
        Directory.CreateDirectory(Path.Combine(root, "TestLib"));
        File.WriteAllText(Path.Combine(root, "TestLib", "package.mo"), "package TestLib \"Test library\"\n  model M\n  end M;\nend TestLib;\n");
        Git.Repository.Init(root);
        using (var repo = new Git.Repository(root))
        {
            Git.Commands.Stage(repo, "*");
            repo.Commit("Initial commit", Author, Author, new Git.CommitOptions());
        }
        new Baseline([]).Save(Path.Combine(root, ".mlqt", "baseline.json"));

        await server.CallAsync("mlqt_load", new() { ["paths"] = new[] { root }, ["withDependencies"] = false });
        var settings = new Dictionary<string, object?> { ["classHasDescription"] = true };
        var check = MlqtMcpServerExtensionsTests.Text(await server.CallAsync("mlqt_check",
            new() { ["classId"] = "TestLib.M", ["settings"] = settings, ["baseline"] = "new" }));
        var listed = MlqtMcpServerExtensionsTests.Text(await server.CallAsync("mlqt_list_findings",
            new() { ["classId"] = "TestLib.M", ["baseline"] = "new" }));

        Assert.Contains("\"status\":\"New\"", check);
        Assert.Contains("\"status\":\"New\"", listed);
    }

    [Fact]
    public async Task ABaselineTheCliWrites_ClassifiesTheSame()
    {
        // What `mlqt baseline` writes: Baseline.FromFindings over the shared pipeline's findings, with
        // the rules recorded - not the server's own output read back.
        using var host = new TestHost();
        var (style, root) = await RepositoryWithBaselineAsync(host, writeBaseline: false);
        var settings = host.Repositories.Repositories.Single().StyleSettings!;
        var models = host.Libraries.Libraries.Single().ModelIds.Select(id => host.Libraries.GetModelById(id)!).ToList();
        var findings = LibraryCheckSession.Check(host.Libraries.CombinedGraph, models, settings,
            host.CustomDictionary, host.DictionaryManager, honorSuppressions: true, repositoryRoot: root);
        Baseline.FromFindings(findings.Where(f => f.ModelId != "TestLib.Added"), settings: settings)
            .Save(Path.Combine(root, ".mlqt", "baseline.json"));

        var library = ToolAssert.Ok<CheckResult>(await style.CheckLibrary(baseline: "new"));
        var oneClass = ToolAssert.Ok<CheckResult>(style.CheckClass("TestLib.Kept", baseline: "new"));

        Assert.Equal("TestLib.Added", Assert.Single(library.Findings).ModelName);
        Assert.Null(library.BaselineWarnings);   // taken with the same rules
        Assert.Empty(oneClass.Findings);
    }

    [Fact]
    public async Task ABaselineThatCannotBeRead_IsSaidSo()
    {
        // "No status" read as "nothing is new" would hide everything an agent introduced.
        using var host = new TestHost();
        var (style, root) = await RepositoryWithBaselineAsync(host, writeBaseline: false);
        File.WriteAllText(Path.Combine(root, ".mlqt", "baseline.json"), "<<<<<<< HEAD\n{");

        var result = ToolAssert.Ok<CheckResult>(await style.CheckLibrary(baseline: "new"));
        var listed = ToolAssert.Ok<FindingsResult>(style.ListFindings());

        Assert.Equal(3, result.FindingCount);
        Assert.Contains("Could not read the baseline", Assert.Single(result.BaselineWarnings!));
        Assert.Contains("Could not read the baseline", Assert.Single(listed.BaselineWarnings!));
    }

    [Fact]
    public async Task ABaselineTakenWithOtherRules_IsSaidSo()
    {
        // ClassHasDescription was off when this baseline was taken, so its findings read as New.
        using var host = new TestHost();
        var (style, root) = await RepositoryWithBaselineAsync(host, writeBaseline: false);
        new Baseline([], rules: new Dictionary<string, ModelicaParser.DataTypes.RuleSeverity>())
            .Save(Path.Combine(root, ".mlqt", "baseline.json"));

        var result = ToolAssert.Ok<CheckResult>(await style.CheckLibrary());

        var warning = Assert.Single(result.BaselineWarnings!);
        Assert.Contains("enabled since: " + RuleIds.ClassDescription, warning);
        Assert.Equal(3, result.Baseline!.New);
    }

    [Fact]
    public async Task TheView_IsReadInAnyCase()
    {
        using var host = new TestHost();
        var (style, _) = await RepositoryWithBaselineAsync(host);

        var result = ToolAssert.Ok<CheckResult>(await style.CheckLibrary(baseline: " New "));

        Assert.Equal("TestLib.Added", Assert.Single(result.Findings).ModelName);
    }

    [Fact]
    public async Task TheNewFindings_AreFoundPastTheCapOnWhatIsReturned()
    {
        // 205 classes of accepted debt and one new: unfiltered, the 200 returned were all debt.
        using var host = new TestHost();
        var classes = string.Concat(Enumerable.Range(0, 205).Select(i => $"  model Old{i:D3}\n  end Old{i:D3};\n"));
        var root = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["TestLib/package.mo"] = $"package TestLib \"Test library\"\n{classes}  model Zed\n  end Zed;\nend TestLib;\n",
        });
        var added = await host.Repositories.AddRepositoryAsync(root, startMonitoring: false);
        await host.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        var style = new StyleTools(host.Libraries, host.CodeReview, host.Repositories, host.CustomDictionary,
            host.DictionaryManager, host.Session,
            new BaselineStatusService(host.Libraries, host.Repositories, host.FileMonitoring));
        await style.SetStyleSettings(new StyleSettingsInput { ClassHasDescription = true });
        Assert.True(ToolAssert.Ok<CheckResult>(await style.CheckLibrary()).Truncated);
        new Baseline(host.CodeReview.LogMessages
                .Where(m => m.ModelName != "TestLib.Zed")
                .Select(m => new BaselineEntry(m.Fingerprint!, m.RuleId!, m.ModelName, m.ElementPath, m.Summary))
                .ToList())
            .Save(Path.Combine(root, ".mlqt", "baseline.json"));

        var result = ToolAssert.Ok<CheckResult>(await style.CheckLibrary(baseline: "new"));

        Assert.Equal("TestLib.Zed", Assert.Single(result.Findings).ModelName);
        Assert.False(result.Truncated);
        Assert.Equal(205, result.Baseline!.NotShown);
    }

    [Fact]
    public async Task MlqtCheck_RefusesAViewThatDoesNotExist()
    {
        using var host = new TestHost();
        await RepositoryWithBaselineAsync(host);
        var tools = new AnalysisFacadeTools(host.Libraries, host.Repositories, host.Resources, host.CodeReview,
            host.CustomDictionary, host.DictionaryManager, host.Impact, host.Session);

        var error = ToolAssert.Error(await tools.Check(checks: ["spelling"], classId: "TestLib.Added", baseline: "fresh"));

        Assert.Contains("all, new, changed", error.Error);
    }

    [Fact]
    public async Task AViewThatDoesNotExist_IsRefused_NamingTheOnesThatDo()
    {
        using var host = new TestHost();
        var (style, _) = await RepositoryWithBaselineAsync(host);

        var error = ToolAssert.Error(await style.CheckLibrary(baseline: "fresh"));

        Assert.Contains("all, new, changed", error.Error);
    }
}
