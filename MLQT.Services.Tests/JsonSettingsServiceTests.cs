using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The settings store both hosts share.
/// </summary>
/// <remarks>
/// <para>It holds every user's project list, repository settings, themes and external-tool paths, and
/// since 7b-3 it is where a MAUI user's settings land when they first open the Photino host. A defect
/// here is not a missing feature, it is somebody's configuration gone — which is why the awkward
/// paths are tested rather than the happy one alone.</para>
///
/// <para>Against a real file in a temporary directory, not a fake: the failure modes worth covering
/// are a corrupt file, a value whose shape has changed, and surviving a restart, and none of those
/// mean anything against a dictionary.</para>
/// </remarks>
public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "mlqt-settings-" + Guid.NewGuid().ToString("N"));

    /// <summary>A store in this test's own directory, never the real one.</summary>
    /// <remarks>
    /// The first version of these tests redirected the <c>LOCALAPPDATA</c> environment variable and
    /// assumed that moved the store. It does not: <c>Environment.GetFolderPath</c> reads the shell's
    /// known folder. The tests therefore ran against the developer's own settings file and destroyed
    /// it - <c>ClearingForgetsEverything</c> emptied it and <c>ACorruptFileStartsFromDefaults</c>
    /// overwrote it with "not json at all". That is the reason the service takes a directory.
    /// </remarks>
    private JsonSettingsService NewStore() => new(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A stored setting's shape. Compared field by field below rather than with record equality: a
    /// positional record compares a <c>List&lt;string&gt;</c> by reference, so two deserialised copies
    /// are never equal and the assertion would fail whatever the service did.
    /// </summary>
    private sealed record Complex(string Name, int Count, List<string> Items);

    private static void AssertSame(Complex expected, Complex? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Name, actual!.Name);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Items, actual.Items);
    }

    [Fact]
    public async Task AValueSurvivesAWriteAndRead()
    {
        var settings = NewStore();

        await settings.SetAsync("key", "value");

        Assert.Equal("value", await settings.GetAsync<string>("key", "default"));
    }

    [Fact]
    public async Task AComplexTypeSurvivesTheRoundTrip()
    {
        // Everything MLQT actually stores is an object - the repository list, the palette, the tool
        // paths. A store that only handled strings would pass a simpler test and fail in use.
        var settings = NewStore();
        var written = new Complex("repositories", 3, ["a", "b"]);

        await settings.SetAsync("complex", written);

        AssertSame(written, await settings.GetAsync<Complex?>("complex", null));
    }

    [Fact]
    public async Task AMissingKeyGivesTheDefault()
    {
        Assert.Equal("fallback", await NewStore().GetAsync("absent", "fallback"));
    }

    [Fact]
    public async Task ValuesSurviveARestart()
    {
        // The property the whole change turns on. An implementation that kept values in memory would
        // pass every test above and lose the user's settings when they closed the application.
        await NewStore().SetAsync("kept", new Complex("x", 1, ["y"]));

        var reopened = NewStore();

        AssertSame(new Complex("x", 1, ["y"]), await reopened.GetAsync<Complex?>("kept", null));
    }

    [Fact]
    public async Task RemovingAKeyForgetsIt()
    {
        var settings = NewStore();
        await settings.SetAsync("key", "value");

        await settings.RemoveAsync("key");

        Assert.Equal("default", await settings.GetAsync("key", "default"));
        Assert.Equal("default", await NewStore().GetAsync("key", "default"));
    }

    [Fact]
    public async Task ClearingForgetsEverything()
    {
        var settings = NewStore();
        await settings.SetAsync("a", "1");
        await settings.SetAsync("b", "2");

        await settings.ClearAsync();

        Assert.Equal("gone", await settings.GetAsync("a", "gone"));
        Assert.Equal("gone", await settings.GetAsync("b", "gone"));
    }

    [Fact]
    public void ItReportsWhereItStores_AndTheDirectoryExists()
    {
        // What the settings.location probe records, and the reason BackingStore exists at all: a
        // round-trip cannot tell persistence from the appearance of it.
        var settings = NewStore();

        Assert.True(Path.IsPathRooted(settings.BackingStore));
        Assert.True(Directory.Exists(Path.GetDirectoryName(settings.BackingStore)));
    }

    [Fact]
    public async Task ACorruptFileStartsFromDefaultsRatherThanThrowing()
    {
        // A settings file truncated by a power cut must not stop the application opening. Starting
        // from defaults is recoverable; a host that will not start is not.
        var path = NewStore().BackingStore;
        await File.WriteAllTextAsync(path, "{ this is not json");

        var settings = NewStore();

        Assert.Equal("default", await settings.GetAsync("anything", "default"));
    }

    [Fact]
    public async Task AValueOfTheWrongShapeGivesTheDefault()
    {
        // A setting whose type changed between releases. Returning the default is what a new user
        // gets anyway; throwing would take out whichever component read it first.
        var settings = NewStore();
        await settings.SetAsync("key", "a plain string");

        Assert.Null(await settings.GetAsync<Complex?>("key", null));
    }

    [Fact]
    public async Task AWriteAfterACorruptFile_LeavesItReadableAgain()
    {
        var path = NewStore().BackingStore;
        await File.WriteAllTextAsync(path, "not json at all");

        var settings = NewStore();
        await settings.SetAsync("key", "value");

        Assert.Equal("value", await NewStore().GetAsync("key", "default"));
    }

    [Fact]
    public async Task AWriteThatCannotReachTheDisk_DoesNotThrow()
    {
        // A settings file the user cannot write - read-only, on a full disk, or locked by a sync
        // client - must not take the application down. The value is lost, which is bad; an
        // unhandled exception from a settings write, which happens on the UI thread while somebody
        // is changing a theme, is worse.
        var settings = NewStore();
        await settings.SetAsync("key", "value");

        var file = new FileInfo(settings.BackingStore) { IsReadOnly = true };
        try
        {
            await settings.SetAsync("key", "a value that cannot be written");
        }
        finally
        {
            file.IsReadOnly = false;
        }

        // Still readable in memory, and the file still holds what it last managed to write.
        Assert.Equal("a value that cannot be written", await settings.GetAsync("key", "default"));
        Assert.Equal("value", await NewStore().GetAsync("key", "default"));
    }

    // ---- the migration (7b-3) ------------------------------------------------------------------

    /// <summary>The shape of one of the real settings, for reading a migrated value back.</summary>
    private sealed record Ui(string Theme);

    /// <summary>What <see cref="MauiPreferencesFile"/> would hand over for a typical user.</summary>
    private static Dictionary<string, string> OldSettings() => new()
    {
        ["Repositories"] = "[{\"Name\":\"MSL\"}]",
        ["UI"] = "{\"Theme\":\"Dark\"}",
    };

    [Fact]
    public async Task MigratingBringsTheOldSettingsAcross()
    {
        var settings = NewStore();

        var copied = settings.MigrateFrom(OldSettings());

        Assert.Equal(["Repositories", "UI"], copied.Order());

        // Read back the way the application reads it, not as the raw string: what has to be true is
        // that the migrated value deserialises into the settings object, which is the whole reason a
        // copy is enough. Comparing the JSON text would pass on a value nothing could load.
        Assert.Equal("Dark", (await settings.GetAsync<Ui?>("UI", null))?.Theme);
    }

    [Fact]
    public void MigratingCopiesEveryKeyItIsGiven_NotAKnownList()
    {
        // The reason there is no key catalogue any more. The first design iterated a written-down list
        // of six names, and the real file turned out to hold a StyleChecking key from an older MLQT
        // and no ReferenceLibraries - so the list was wrong in both directions on the one machine it
        // was checked against. A migration that moves what it finds cannot be wrong about what to
        // look for.
        var settings = NewStore();

        var copied = settings.MigrateFrom(new Dictionary<string, string>
        {
            ["Repositories"] = "1",
            ["AKeyNobodyWroteDown"] = "2",
            ["StyleChecking"] = "3",
        });

        Assert.Equal(3, copied.Count);
    }

    [Fact]
    public async Task MigratingNeverOverwritesASettingAlreadyHere()
    {
        // Anything in this store is newer by definition - the MAUI build cannot write to it - so a
        // value the user has already changed in the new host wins.
        var settings = NewStore();
        await settings.SetAsync("UI", "the new host's value");

        var copied = settings.MigrateFrom(OldSettings());

        Assert.Equal(["Repositories"], copied);
        Assert.Equal("the new host's value", await settings.GetAsync<string?>("UI", null));
    }

    [Fact]
    public void MigratingRunsOnce()
    {
        var settings = NewStore();

        settings.MigrateFrom(OldSettings());

        Assert.Empty(settings.MigrateFrom(new Dictionary<string, string> { ["Something"] = "else" }));
    }

    [Fact]
    public void HasMigratedAnswersBeforeAnybodyGoesLookingForTheOldFile()
    {
        // The host asks this first. MigrateFrom answers the same question and is safe to call twice,
        // but its argument is the contents of the old file - so finding and reading that file happens
        // before the guard inside it is reached, on every launch, for ever. On Linux there is never
        // anything to find, which makes it a search that can only ever fail.
        var settings = NewStore();
        Assert.False(settings.HasMigrated);

        settings.MigrateFrom(OldSettings());

        Assert.True(settings.HasMigrated);
    }

    [Fact]
    public void HasMigratedIsTrueEvenWhenThereWasNothingToBringAcross()
    {
        // "There was nothing" is an answer, and re-deriving it every launch is the cost this avoids.
        var settings = NewStore();

        Assert.Empty(settings.MigrateFrom(new Dictionary<string, string>()));
        Assert.True(settings.HasMigrated);
    }

    [Fact]
    public void HasMigratedSurvivesARestart()
    {
        // The marker is in the file, not in memory - the same property MigratingRunsOnceAcrossRestarts
        // asserts of the migration itself, asked of the short-circuit that now stands in front of it.
        NewStore().MigrateFrom(OldSettings());

        Assert.True(NewStore().HasMigrated);
    }

    [Fact]
    public void MigratingRunsOnceAcrossRestarts()
    {
        // The marker has to be in the file, not in memory: the migration runs at startup, and an
        // in-memory marker would re-run on every launch and undo the user's later changes - including
        // bringing back settings they had deleted.
        NewStore().MigrateFrom(OldSettings());

        Assert.Empty(NewStore().MigrateFrom(OldSettings()));
    }

    [Fact]
    public void FindingNothingToMigrate_StillCounts()
    {
        // A user with no MAUI install gets the marker too, so the next launch does not go looking for
        // a file that was not there the first time either.
        var settings = NewStore();

        Assert.Empty(settings.MigrateFrom(new Dictionary<string, string>()));
        Assert.Empty(settings.MigrateFrom(OldSettings()));
    }

    [Fact]
    public async Task TheMarkerSharesTheFileWithTheSettings()
    {
        // The marker is a key in the same dictionary, which is what makes it survive a restart with no
        // second file to keep in step. This is the shape that has to hold: both readable afterwards.
        var settings = NewStore();
        settings.MigrateFrom(OldSettings());

        var reopened = NewStore();

        Assert.True(await reopened.GetAsync(JsonSettingsService.MigratedKey, false));
        Assert.Equal("Dark", (await reopened.GetAsync<Ui?>("UI", null))?.Theme);
    }
}
