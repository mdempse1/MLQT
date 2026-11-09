using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The repository's accepted spellings, through mlqt_style_settings. The word list existed - each
/// repository's committed <c>.mlqt/dictionary.txt</c>, read by the desktop app, the CLI and every check
/// here - but no tool wrote to it, so an agent accepted words class by class: those accepted for one
/// library had to be accepted again for the next, and generated documentation gave 189 findings.
/// </summary>
public class RepositorySpellingTests
{
    private static async Task<(StyleTools Style, string Root)> Load(TestHost host)
    {
        // One repository, two libraries, both using the same domain word.
        var root = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["Powertrain/package.mo"] = "within;\npackage Powertrain \"Each halfshaft is modelled.\"\nend Powertrain;\n",
            ["Vehicle/package.mo"] = "within;\npackage Vehicle \"The rear halfshaft drives the wheel.\"\nend Vehicle;\n",
        });
        var added = await host.Repositories.AddRepositoryAsync(root, startMonitoring: false);
        await host.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new ModelicaGraph.StyleCheckingSettings
        {
            SpellCheckDescription = true,
            SpellCheckLanguages = ["en_US"],
        };
        return (new StyleTools(host.Libraries, host.CodeReview, host.Repositories, host.CustomDictionary,
            host.DictionaryManager, host.Session), root);
    }

    private static async Task<bool> Reports(StyleTools style, string library, string word) =>
        ToolAssert.Ok<CheckResult>(await style.CheckLibrary(library)).Findings.Any(f => f.Summary.Contains(word));

    [Fact]
    public async Task AWordAcceptedForTheRepository_IsAcceptedInEveryLibraryInIt()
    {
        using var host = new TestHost();
        var (style, root) = await Load(host);
        Assert.True(await Reports(style, "Powertrain", "halfshaft"));
        Assert.True(await Reports(style, "Vehicle", "halfshaft"));

        var res = ToolAssert.Ok<SetStyleSettingsResult>(await style.SetStyleSettings(null, acceptSpellings: ["halfshaft"]));

        Assert.Contains("halfshaft", res.AcceptedSpellings);
        Assert.Contains("halfshaft", File.ReadAllText(Path.Combine(root, ".mlqt", "dictionary.txt")));
        Assert.Contains("mlqt_check", res.Note);
        Assert.Null(res.Persisted);   // no settings were passed, so none were written
        Assert.Null(res.Path);
        Assert.False(await Reports(style, "Powertrain", "halfshaft"));
        Assert.False(await Reports(style, "Vehicle", "halfshaft"));
    }

    [Fact]
    public async Task ReadingTheSettings_ListsTheAcceptedWords()
    {
        using var host = new TestHost();
        var (style, _) = await Load(host);
        ToolAssert.Ok<SetStyleSettingsResult>(await style.SetStyleSettings(null, acceptSpellings: ["halfshaft", "flexplate"]));

        var read = ToolAssert.Ok<StyleSettingsResult>(style.GetStyleSettings());

        Assert.Equal(["flexplate", "halfshaft"], read.AcceptedSpellings);
    }

    [Fact]
    public async Task AWordCanBeTakenOutAgain()
    {
        using var host = new TestHost();
        var (style, _) = await Load(host);
        ToolAssert.Ok<SetStyleSettingsResult>(await style.SetStyleSettings(null, acceptSpellings: ["halfshaft"]));

        var res = ToolAssert.Ok<SetStyleSettingsResult>(await style.SetStyleSettings(null, removeSpellings: ["halfshaft"]));

        Assert.DoesNotContain("halfshaft", res.AcceptedSpellings);
        Assert.True(await Reports(style, "Vehicle", "halfshaft"));
    }

    [Fact]
    public async Task APhrase_IsRefused_ForTheListIsOfWords()
    {
        using var host = new TestHost();
        var (style, _) = await Load(host);

        var err = ToolAssert.Error(await style.SetStyleSettings(null, acceptSpellings: ["rear halfshaft"]));

        Assert.Contains("one word", err.Error);
        Assert.Empty(ToolAssert.Ok<StyleSettingsResult>(style.GetStyleSettings()).AcceptedSpellings);
    }

    [Fact]
    public async Task ADictionaryThatCannotBeWritten_SaysTheSettingsWereStillWritten()
    {
        // The agent should not set them again thinking the whole call failed.
        using var host = new TestHost();
        var (style, root) = await Load(host);
        Directory.CreateDirectory(Path.Combine(root, ".mlqt", "dictionary.txt"));   // a folder: the write fails

        var err = ToolAssert.Error(await style.SetStyleSettings(new StyleSettingsInput { SpellCheckDocumentation = true },
            acceptSpellings: ["halfshaft"]));

        Assert.Contains("written to .mlqt/settings.json", err.Error);
        Assert.True(File.Exists(Path.Combine(root, ".mlqt", "settings.json")));
        Assert.Empty(ToolAssert.Ok<StyleSettingsResult>(style.GetStyleSettings()).AcceptedSpellings);
    }

    [Fact]
    public async Task NothingToChange_IsRefused_WhenAskedToChange()
    {
        using var host = new TestHost();
        var (style, _) = await Load(host);

        Assert.Contains("acceptSpellings", ToolAssert.Error(await style.SetStyleSettings(null)).Error);
    }

    [Fact]
    public async Task TheTool_ReadsWithNothingToChange_AndWritesWithWords()
    {
        using var host = new TestHost();
        await Load(host);
        var facade = new SessionFacadeTools(host.Libraries, host.Repositories, host.Resources, host.CodeReview,
            host.CustomDictionary, host.DictionaryManager, host.Session);

        ToolAssert.Ok<StyleSettingsResult>(await facade.StyleSettings());
        var written = ToolAssert.Ok<SetStyleSettingsResult>(await facade.StyleSettings(acceptSpellings: ["driveability"]));

        Assert.Contains("driveability", written.AcceptedSpellings);
    }
}
