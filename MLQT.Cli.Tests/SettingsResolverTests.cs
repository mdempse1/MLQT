using MLQT.Cli;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// Where a run's accepted spellings are read from. They sit in the same <c>.mlqt</c> directory as the
/// settings, so a run told to use a repository's settings has to read that repository's words too —
/// otherwise CI checks a sub-library against the team's rules and none of the team's vocabulary, and
/// reports every accepted term as a misspelling.
/// </summary>
public class SettingsResolverTests : IDisposable
{
    private readonly string _repo = Path.Combine(
        Path.GetTempPath(), "mlqt-resolver", Guid.NewGuid().ToString("N"));

    private string Library => Path.Combine(_repo, "MyLib");

    public SettingsResolverTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo, ".mlqt"));
        Directory.CreateDirectory(Library);
        File.WriteAllText(Path.Combine(_repo, ".mlqt", "settings.json"), "{\"ClassHasDescription\":true}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AConfigInARepository_BringsThatRepositorysWords()
    {
        var resolved = SettingsResolver.Resolve(Library, Path.Combine(_repo, ".mlqt", "settings.json"));

        Assert.True(resolved.Settings.ClassHasDescription);
        Assert.Equal(_repo, resolved.DictionaryRoot);
    }

    [Fact]
    public void SettingsBesideTheLibrary_KeepTheLibraryAsTheRoot()
    {
        Directory.CreateDirectory(Path.Combine(Library, ".mlqt"));
        File.WriteAllText(Path.Combine(Library, ".mlqt", "settings.json"), "{}");

        var resolved = SettingsResolver.Resolve(Library, configPath: null);

        Assert.Equal(Library, resolved.DictionaryRoot);
    }

    [Fact]
    public void NoSettingsAtAll_LeavesTheLibraryAsTheRoot()
    {
        var resolved = SettingsResolver.Resolve(Library, configPath: null);

        Assert.Equal("built-in defaults", resolved.Source);
        Assert.Equal(Library, resolved.DictionaryRoot);
    }

    [Fact]
    public void AConfigKeptOutsideAnyMlqtDirectory_LeavesTheLibraryAsTheRoot()
    {
        // A shared rules file has no accepted spellings of its own, so there is nothing to read
        // beside it and the library stays the place to look.
        var shared = Path.Combine(_repo, "shared-rules.json");
        File.WriteAllText(shared, "{}");

        var resolved = SettingsResolver.Resolve(Library, shared);

        Assert.Equal(Library, resolved.DictionaryRoot);
    }
}
