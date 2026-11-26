using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using MLQT.Services.Checking;

namespace MLQT.McpServer.Tests;

/// <summary>
/// An agent can tell whether what it writes will be formatted, and is told when it is not.
/// </summary>
/// <remarks>
/// A write is formatted only where the repository's settings have ApplyFormattingRules on, which is off
/// unless a repository turns it on - and nothing an agent saw said which. The guidance opened with
/// "edits format themselves", so one wrote class after class into a repository that did not, took the
/// imports-after-extends findings on each for something MLQT would put right, and ran mlqt_format
/// before every commit to catch up.
/// </remarks>
public class FormatsWritesTests
{
    // The class, as an agent writes it: its import after its extends clause.
    private const string Jumbled = "within P;\nmodel M \"m\"\n  extends Base;\n  import Modelica.Units.SI;\nend M;\n";

    private static async Task<(TestHost Host, string Root)> Repository(bool applyFormattingRules)
    {
        var host = new TestHost();
        var root = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["P/package.mo"] = "within;\npackage P \"p\"\nend P;\n",
            ["P/Base.mo"] = "within P;\npartial model Base \"b\"\nend Base;\n",
            ["P/M.mo"] = Jumbled,
            ["P/package.order"] = "Base\nM\n",
            [".mlqt/settings.json"] =
                $"{{\n  \"ApplyFormattingRules\": {(applyFormattingRules ? "true" : "false")},\n" +
                "  \"OneOfEachSection\": true,\n  \"ImportStatementsFirst\": true,\n  \"ComponentsBeforeClasses\": true\n}\n",
        });
        return (host, root);
    }

    private static SessionTools Session(TestHost host) => new(host.Libraries, host.Repositories, host.Resources, host.Session);

    private static StyleTools Style(TestHost host) => new(host.Libraries, host.CodeReview, host.Repositories,
        host.CustomDictionary, host.DictionaryManager, host.Session,
        new BaselineStatusService(host.Libraries, host.Repositories, host.FileMonitoring));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoadAndStatus_SayWhetherTheRepositoryFormatsWrites(bool applyFormattingRules)
    {
        var (host, root) = await Repository(applyFormattingRules);
        using var _ = host;

        var load = await Session(host).LoadRepository(root);

        Assert.Equal(applyFormattingRules, load.FormatsWrites);
        Assert.Equal(applyFormattingRules, Assert.Single(Session(host).ListRepositories()).FormatsWrites);
    }

    [Fact]
    public async Task ACheck_OfAClassWhoseWritesAreNotFormatted_SaysMlqtFormatFixesItsLayout()
    {
        var (host, root) = await Repository(applyFormattingRules: false);
        using var _ = host;
        await Session(host).LoadRepository(root);

        var check = ToolAssert.Ok<CheckResult>(Style(host).CheckClass("P.M"));

        Assert.Contains(check.Findings, f => f.RuleId == "MLQT.Style.ImportStatementsFirst");
        Assert.NotNull(check.Note);
        Assert.Contains("mlqt_format", check.Note);
        Assert.Contains("ApplyFormattingRules off", check.Note);
        Assert.Contains("MLQT.Style.ImportStatementsFirst", check.Note);
    }

    [Fact]
    public async Task ACheck_WhereWritesAreFormatted_HasNoNote()
    {
        // The same finding, in a repository that formats its writes: the next write puts it right, so
        // there is nothing to tell.
        var (host, root) = await Repository(applyFormattingRules: true);
        using var _ = host;
        await Session(host).LoadRepository(root);

        var check = ToolAssert.Ok<CheckResult>(Style(host).CheckClass("P.M"));

        Assert.Contains(check.Findings, f => f.RuleId == "MLQT.Style.ImportStatementsFirst");
        Assert.Null(check.Note);
    }

    [Fact]
    public async Task ALibraryCheck_SaysItToo()
    {
        var (host, root) = await Repository(applyFormattingRules: false);
        using var _ = host;
        await Session(host).LoadRepository(root);

        var check = ToolAssert.Ok<CheckResult>(await Style(host).CheckLibrary("P"));

        Assert.Contains("mlqt_format", check.Note);
    }

    /// <summary>
    /// MLQT.Style.ExtendsAtTop has no formatter flag of its own: it is governed by
    /// MLQT.Style.ImportStatementsFirst and the formatter fixes it with it. Asked only for the flag, the
    /// note said nothing about a class whose one finding was an extends clause below a component.
    /// </summary>
    [Fact]
    public async Task ACheck_WhoseOnlyLayoutFindingIsAGovernedRule_StillSaysMlqtFormatFixesIt()
    {
        var (host, root) = await Repository(applyFormattingRules: false);
        using var _ = host;
        File.WriteAllText(Path.Combine(root, "P", "M.mo"), "within P;\nmodel M \"m\"\n  Real x;\n  extends Base;\nend M;\n");
        await Session(host).LoadRepository(root);

        var check = ToolAssert.Ok<CheckResult>(Style(host).CheckClass("P.M"));

        Assert.Equal("MLQT.Style.ExtendsAtTop", Assert.Single(check.Findings).RuleId);
        Assert.Contains("MLQT.Style.ExtendsAtTop", check.Note);
    }

    [Fact]
    public async Task ListedFindings_CarryTheNoteToo()
    {
        // An agent can read the findings a check stored from mlqt_list_findings rather than the check.
        var (host, root) = await Repository(applyFormattingRules: false);
        using var _ = host;
        await Session(host).LoadRepository(root);
        Style(host).CheckClass("P.M");

        var listed = ToolAssert.Ok<FindingsResult>(Style(host).ListFindings(classId: "P.M"));

        Assert.Contains("mlqt_format", listed.Note);
        Assert.Null(ToolAssert.Ok<FindingsResult>(Style(host).ListFindings(classId: "P.Base")).Note);
    }

    /// <summary>
    /// The note counts what the result reports. Worked out before the baseline view, it counted the
    /// accepted debt baseline 'new' had just left out, and said "4 finding(s)" beside two.
    /// </summary>
    [Fact]
    public async Task TheNote_CountsOnlyTheFindingsTheViewShows()
    {
        var (host, root) = await Repository(applyFormattingRules: false);
        using var _ = host;
        File.WriteAllText(Path.Combine(root, "P", "Old.mo"), Jumbled.Replace("model M \"m\"", "model Old \"o\"").Replace("end M;", "end Old;"));
        File.WriteAllText(Path.Combine(root, "P", "package.order"), "Base\nM\nOld\n");
        LibGit2Sharp.Repository.Init(root);
        var author = new LibGit2Sharp.Signature("Test Author", "test@test.com", DateTimeOffset.Now);
        using (var repo = new LibGit2Sharp.Repository(root))
        {
            LibGit2Sharp.Commands.Stage(repo, "*");
            repo.Commit("Initial commit", author, author, new LibGit2Sharp.CommitOptions());
        }
        await Session(host).LoadRepository(root);
        var style = Style(host);

        // Each class has two: its import after its extends is both ImportStatementsFirst and
        // ExtendsAtTop. The baseline accepts Old's; M's are new.
        var all = ToolAssert.Ok<CheckResult>(await style.CheckLibrary("P"));
        Assert.Contains("4 finding(s)", all.Note);
        new Baseline(host.CodeReview.LogMessages
                .Where(m => m.ModelName == "P.Old")
                .Select(m => new BaselineEntry(m.Fingerprint!, m.RuleId!, m.ModelName, m.ElementPath, m.Summary))
                .ToList())
            .Save(Path.Combine(root, ".mlqt", "baseline.json"));

        var onlyNew = ToolAssert.Ok<CheckResult>(await style.CheckLibrary("P", baseline: "new"));

        Assert.Equal(2, onlyNew.FindingCount);
        Assert.Contains("2 finding(s)", onlyNew.Note);
    }

    [Fact]
    public void TheGuidance_NoLongerSaysEditsFormatThemselves()
    {
        var formatting = GuidanceTools.Guidance["formatting"];

        Assert.DoesNotContain("Edits format themselves", formatting);
        Assert.Contains("formatsWrites", formatting);
        Assert.Contains("formatsWrites", GuidanceTools.Guidance["workflows"]);
    }
}
