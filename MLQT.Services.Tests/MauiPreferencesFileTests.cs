using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Reading what the MAUI build left behind.
/// </summary>
/// <remarks>
/// <para>Phase 7b-3. This runs once per user, on the launch where they first open the Photino host,
/// and it is the only chance there is: get it wrong and their projects, repositories, themes and tool
/// paths are gone, with no second attempt because the marker will have been written. So the tests are
/// about the ways it can find nothing — a missing folder, a different publisher, an unparseable file —
/// rather than about the happy path, which is one line.</para>
///
/// <para>The sample is the real shape, taken from an actual install: an outer object keyed by MAUI
/// container name, whose default container is the empty string, holding the JSON strings MLQT stored.
/// Including <c>StyleChecking</c>, which no current version of MLQT reads and which the first design's
/// hand-written key list did not have.</para>
/// </remarks>
public class MauiPreferencesFileTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "mlqt-maui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private const string Sample = """
        {
          "": {
            "Repositories": "[{\"Name\":\"MSL\"}]",
            "UI": "{\"Theme\":\"Dark\"}",
            "StyleChecking": "{\"CheckNaming\":true}"
          }
        }
        """;

    /// <summary>Writes a preferences file where MAUI would have put it, under the given publisher.</summary>
    private string Given(string publisher, string content = Sample)
    {
        var path = Path.Combine(_root, publisher, MauiPreferencesFile.ApplicationId, "Settings", "preferences.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ---- finding it ----------------------------------------------------------------------------

    [Fact]
    public void TheFileIsFoundUnderWhateverThePublisherIs()
    {
        // "User Name" is what a default MAUI manifest produces - Publisher="CN=User Name" - and it is
        // what MLQT ships with today. The publisher is searched for rather than hard-coded because a
        // build that set a real one would file its settings somewhere else, and the symptom would be
        // a migration that silently found nothing.
        var expected = Given("User Name");

        Assert.Equal(expected, MauiPreferencesFile.Locate(_root));
    }

    [Fact]
    public void ADifferentPublisherIsStillFound()
    {
        var expected = Given("CN=Claytex Services Limited");

        Assert.Equal(expected, MauiPreferencesFile.Locate(_root));
    }

    [Fact]
    public void NoMauiInstallFindsNothing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Microsoft", "Edge"));

        Assert.Null(MauiPreferencesFile.Locate(_root));
    }

    [Fact]
    public void AMissingLocalAppDataFindsNothing()
    {
        Assert.Null(MauiPreferencesFile.Locate(Path.Combine(_root, "nothing here")));
    }

    [Fact]
    public void TheApplicationFolderWithoutASettingsFileFindsNothing()
    {
        // An install that has run but never written a setting. The folder exists; the file does not.
        Directory.CreateDirectory(Path.Combine(_root, "User Name", MauiPreferencesFile.ApplicationId, "Settings"));

        Assert.Null(MauiPreferencesFile.Locate(_root));
    }

    [Fact]
    public void TwoInstalls_TheMostRecentlyUsedWins()
    {
        // A machine that has run MLQT under two publishers - a rename, or a side-by-side build. Either
        // answer loses something; the newest is the one whose settings the user last saw.
        var old = Given("Old Publisher");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));

        var current = Given("User Name");
        File.SetLastWriteTimeUtc(current, DateTime.UtcNow);

        Assert.Equal(current, MauiPreferencesFile.Locate(_root));
    }

    // ---- reading it ----------------------------------------------------------------------------

    [Fact]
    public void EverySettingInTheFileIsRead()
    {
        var settings = MauiPreferencesFile.Read(Given("User Name"));

        Assert.Equal(["Repositories", "StyleChecking", "UI"], settings.Keys.Order());
    }

    [Fact]
    public void TheValuesComeBackAsTheJsonMlqtStored()
    {
        // Not converted, not re-serialised: the strings MAUI held are already what JsonSettingsService
        // holds, which is why the migration is a copy.
        var settings = MauiPreferencesFile.Read(Given("User Name"));

        Assert.Equal("{\"Theme\":\"Dark\"}", settings["UI"]);
    }

    [Fact]
    public void AKeyNoVersionOfMlqtReadsIsStillBroughtAcross()
    {
        // StyleChecking is legacy - nothing in the current code asks for it. It comes across anyway,
        // because deciding what is worth keeping is how the six-key list managed to be wrong.
        var settings = MauiPreferencesFile.Read(Given("User Name"));

        Assert.Equal("{\"CheckNaming\":true}", settings["StyleChecking"]);
    }

    [Fact]
    public void ANamedContainerIsReadToo()
    {
        // MLQT only ever used the default container, whose name is the empty string. Reading the
        // others costs nothing, and a key silently dropped costs a user their settings.
        var path = Given("User Name", """{"": {"UI": "1"}, "shared": {"Other": "2"}}""");

        Assert.Equal(["Other", "UI"], MauiPreferencesFile.Read(path).Keys.Order());
    }

    [Fact]
    public void NonStringValuesAreSkipped()
    {
        // MAUI writes every preference as a string. Anything else is not a setting MLQT wrote, and
        // handing it to the store would put a value there that no reader can deserialise.
        var path = Given("User Name", """{"": {"UI": "1", "Count": 3, "Missing": null, "Empty": ""}}""");

        Assert.Equal(["UI"], MauiPreferencesFile.Read(path).Keys);
    }

    [Fact]
    public void AnUnparseableFileMigratesNothingRatherThanFailing()
    {
        // The host has to open. A user who lost their settings to a corrupt file still has an
        // application, and their MAUI install is untouched so nothing is destroyed by trying.
        var path = Given("User Name", "this is not json");

        Assert.Empty(MauiPreferencesFile.Read(path));
    }

    [Fact]
    public void AFileOfTheWrongShapeMigratesNothing()
    {
        var path = Given("User Name", """["not", "an", "object"]""");

        Assert.Empty(MauiPreferencesFile.Read(path));
    }

    [Fact]
    public void AMissingFileMigratesNothing()
    {
        Assert.Empty(MauiPreferencesFile.Read(Path.Combine(_root, "gone.dat")));
    }

    [Fact]
    public void TheFileIsNeverWritten()
    {
        // A user who goes back to an older MLQT release must find everything where they left it. That
        // matters more during a migration than tidying up does.
        var path = Given("User Name");
        var before = File.ReadAllText(path);
        var written = File.GetLastWriteTimeUtc(path);

        MauiPreferencesFile.Read(path);

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }
}
