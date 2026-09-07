using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class StyleSettingsTests
{
    private const string Package = """
        within;
        package TestLib "Test library"
          model Base "Base model"
            Real b "state";
          equation
            b = time;
          end Base;
        end TestLib;
        """;

    private static async Task<(StyleTools style, string repoId, string localPath)> LoadRepo(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["TestLib/package.mo"] = Package });
        var res = await host.Repositories.AddRepositoryAsync(dir, startMonitoring: false);
        await host.Repositories.LoadLibrariesAsync(res.Repository!.Id);
        var style = new StyleTools(host.Libraries, host.CodeReview, host.Repositories, host.CustomDictionary, host.DictionaryManager, host.Session);
        return (style, res.Repository.Id, res.Repository.LocalPath);
    }

    [Fact]
    public async Task GetStyleSettings_ReadsFromRepoMlqtSettings()
    {
        using var host = new TestHost();
        var (style, _, _) = await LoadRepo(host);

        var res = ToolAssert.Ok<StyleSettingsResult>(style.GetStyleSettings());
        Assert.Contains(".mlqt", res.Source);
        Assert.False(res.Settings.ClassHasDocumentationInfo); // fresh repo -> defaults
    }

    [Fact]
    public async Task SetStyleSettings_PersistsToMlqt_AndRoundTrips()
    {
        using var host = new TestHost();
        var (style, _, localPath) = await LoadRepo(host);

        var set = ToolAssert.Ok<SetStyleSettingsResult>(await style.SetStyleSettings(
            new StyleSettingsInput { ClassHasDocumentationInfo = true, SpellCheckLanguages = new[] { "en_GB" } }));

        Assert.True(set.Persisted);
        Assert.True(File.Exists(Path.Combine(localPath, ".mlqt", "settings.json")));

        var got = ToolAssert.Ok<StyleSettingsResult>(style.GetStyleSettings());
        Assert.True(got.Settings.ClassHasDocumentationInfo);
        Assert.Contains("en_GB", got.Settings.SpellCheckLanguages!);
    }

    [Fact]
    public async Task CheckClass_DefaultsToRepoSettings()
    {
        using var host = new TestHost();
        var (style, _, _) = await LoadRepo(host);
        await style.SetStyleSettings(new StyleSettingsInput { ClassHasDocumentationInfo = true });

        // No explicit settings -> uses the repo's persisted rules.
        var res = ToolAssert.Ok<CheckResult>(style.CheckClass("TestLib.Base"));
        Assert.True(res.FindingCount >= 1);
    }

    [Fact]
    public async Task SetStyleSettings_PreservesNamingConfig()
    {
        using var host = new TestHost();
        var (style, repoId, _) = await LoadRepo(host);
        var repo = host.Repositories.GetRepository(repoId)!;
        var presetBefore = repo.StyleSettings!.NamingConvention.PresetName;

        await style.SetStyleSettings(new StyleSettingsInput { ClassHasDescription = true });

        // Only the toggles change; the naming-convention config is left intact.
        Assert.Equal(presetBefore, repo.StyleSettings.NamingConvention.PresetName);
        Assert.True(repo.StyleSettings.ClassHasDescription);
    }

    [Fact]
    public void SetStyleSettings_NoRepository_GuidesToLoadRepository()
    {
        using var host = new TestHost();
        var style = new StyleTools(host.Libraries, host.CodeReview, host.Repositories, host.CustomDictionary, host.DictionaryManager, host.Session);

        var err = ToolAssert.Error(style.SetStyleSettings(new StyleSettingsInput { ClassHasDescription = true })
            .GetAwaiter().GetResult());
        Assert.Contains("repository", err.Error);
    }
}
