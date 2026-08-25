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
    public void AConfigKeptOutsideAnyMlqtDirectory_LeavesTheLibraryAsTheRoot()
    {
        // A shared rules file has no accepted spellings of its own, so there is nothing to read
        // beside it and the library stays the place to look.
        var shared = Path.Combine(_repo, "shared-rules.json");
        File.WriteAllText(shared, "{}");

        var resolved = SettingsResolver.Resolve(Library, shared);

        Assert.Equal(Library, resolved.DictionaryRoot);
    }

    [Fact]
    public void SettingsAboveTheLibrary_AreFound()
    {
        // What the app does: settings belong to a repository, and a repository usually holds several
        // libraries with one .mlqt at its root. Looking only in the library meant the CLI silently
        // used built-in defaults where the app used the team's rules.
        var resolved = SettingsResolver.Resolve(Library, configPath: null);

        Assert.Equal(Path.Combine(_repo, ".mlqt", "settings.json"), resolved.Source);
        Assert.True(resolved.Settings.ClassHasDescription);
        Assert.Equal(_repo, resolved.DictionaryRoot);
    }

    [Fact]
    public void SettingsBesideTheLibrary_WinOverThoseAbove()
    {
        Directory.CreateDirectory(Path.Combine(Library, ".mlqt"));
        File.WriteAllText(Path.Combine(Library, ".mlqt", "settings.json"), "{\"ClassHasIcon\":true}");

        var resolved = SettingsResolver.Resolve(Library, configPath: null);

        Assert.True(resolved.Settings.ClassHasIcon);
        Assert.False(resolved.Settings.ClassHasDescription);
        Assert.Equal(Library, resolved.DictionaryRoot);
    }

    [Fact]
    public void TheWalkStopsAtAWorkingCopyRoot()
    {
        // A checkout must never pick up a settings file belonging to something outside it — one left
        // in a shared parent folder, or in a home directory.
        var checkout = Path.Combine(_repo, "Vendored");
        Directory.CreateDirectory(Path.Combine(checkout, ".git"));
        var library = Path.Combine(checkout, "TheirLib");
        Directory.CreateDirectory(library);

        var resolved = SettingsResolver.Resolve(library, configPath: null);

        Assert.Equal("built-in defaults", resolved.Source);
        Assert.Equal(library, resolved.DictionaryRoot);
    }
}
