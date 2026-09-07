using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Tests for <see cref="CustomDictionaryService"/> — each repository's accepted spellings, stored at
/// <c>.mlqt/dictionary.txt</c> and committed with the code.
///
/// <para>The list moved here from a single machine-wide file so the desktop app and CI cannot be
/// given different inputs: a word one of them accepted and the other had never heard of produced
/// different spelling findings for the same library, with nothing in either result to show why. Most
/// of what these tests pin down is that separation — words belonging to one repository and not
/// leaking into another.</para>
/// </summary>
public class CustomDictionaryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-dictionary", Guid.NewGuid().ToString("N"));

    private readonly string _alpha;
    private readonly string _beta;

    public CustomDictionaryServiceTests()
    {
        _alpha = Path.Combine(_root, "Alpha");
        _beta = Path.Combine(_root, "Beta");
        Directory.CreateDirectory(_alpha);
        Directory.CreateDirectory(_beta);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private CustomDictionaryService NewService(string? legacyPath = null) => new(legacyPath);

    #region Storage location

    [Fact]
    public void PathFor_IsBesideTheRepositorySettings()
    {
        var service = NewService();

        Assert.Equal(Path.Combine(_alpha, ".mlqt", "dictionary.txt"), service.PathFor(_alpha));
    }

    [Fact]
    public async Task AddWord_CreatesTheFileAndPersistsIt()
    {
        var service = NewService();

        await service.AddWordAsync(_alpha, "enthalpy");

        var path = service.PathFor(_alpha);
        Assert.True(File.Exists(path));
        Assert.Contains("enthalpy", await File.ReadAllLinesAsync(path));
    }

    [Fact]
    public async Task Words_AreWrittenSortedOnePerLine()
    {
        // The file is reviewed and merged like any other source, so its order must not depend on the
        // sequence words happened to be added in.
        var service = NewService();

        await service.AddWordAsync(_alpha, "zeta");
        await service.AddWordAsync(_alpha, "alpha");
        await service.AddWordAsync(_alpha, "mu");

        Assert.Equal(["alpha", "mu", "zeta"], await File.ReadAllLinesAsync(service.PathFor(_alpha)));
    }

    [Fact]
    public void WordsFor_ReadsAListWrittenByHand()
    {
        // The file is meant to be edited and committed by people, not only through the app.
        Directory.CreateDirectory(Path.Combine(_alpha, ".mlqt"));
        File.WriteAllLines(Path.Combine(_alpha, ".mlqt", "dictionary.txt"),
            ["# terms agreed with the modelling team", "SOC", "", "  enthalpy  "]);

        var words = NewService().WordsFor(_alpha);

        Assert.Equal(["enthalpy", "SOC"], words);
    }

    #endregion

    #region Separation between repositories

    [Fact]
    public async Task AWordAcceptedInOneRepository_DoesNotApplyToAnother()
    {
        var service = NewService();

        await service.AddWordAsync(_alpha, "enthalpy");

        Assert.Contains("enthalpy", service.WordsFor(_alpha));
        Assert.Empty(service.WordsFor(_beta));
    }

    [Fact]
    public async Task RemovingAWordFromOneRepository_LeavesTheOtherAlone()
    {
        var service = NewService();
        await service.AddWordAsync(_alpha, "enthalpy");
        await service.AddWordAsync(_beta, "enthalpy");

        await service.RemoveWordAsync(_alpha, "enthalpy");

        Assert.Empty(service.WordsFor(_alpha));
        Assert.Contains("enthalpy", service.WordsFor(_beta));
    }

    [Fact]
    public void WordsFor_NoRepository_IsEmptyRatherThanSomeoneElses()
    {
        var service = NewService();

        Assert.Empty(service.WordsFor(null));
        Assert.Empty(service.WordsFor(""));
    }

    [Fact]
    public void WordsFor_ARepositoryWithNoList_IsEmpty()
    {
        Assert.Empty(NewService().WordsFor(_beta));
    }

    #endregion

    #region Change notification

    [Fact]
    public async Task Changes_AnnounceWhichRepositoryTheyBelongTo()
    {
        // The desktop app caches a spell checker per repository and has to know which one to discard.
        var service = NewService();
        var changed = new List<string>();
        service.OnDictionaryChanged += root => changed.Add(root);

        await service.AddWordAsync(_alpha, "enthalpy");

        Assert.Equal([_alpha], changed);
    }

    [Fact]
    public async Task AddingAWordAlreadyPresent_AnnouncesNothing()
    {
        var service = NewService();
        await service.AddWordAsync(_alpha, "enthalpy");

        var changed = 0;
        service.OnDictionaryChanged += _ => changed++;
        await service.AddWordAsync(_alpha, "ENTHALPY");

        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task AnEditMadeOutsideTheAppIsPickedUpAndAnnounced()
    {
        // Committed lists arrive by version control update and get edited in a text editor, so the
        // file — not the first read of it — is the authority. Whoever cached a spell checker built
        // from the old list has to be told, or the word stays reported while the settings page shows
        // it as accepted.
        var service = NewService();
        await service.AddWordAsync(_alpha, "enthalpy");

        var changed = new List<string>();
        service.OnDictionaryChanged += root => changed.Add(root);

        File.WriteAllLines(service.PathFor(_alpha), ["enthalpy", "exergy"]);

        Assert.Equal(["enthalpy", "exergy"], service.WordsFor(_alpha));
        Assert.Equal([_alpha], changed);
    }

    [Fact]
    public async Task AnUnchangedFileIsNotAnnouncedAgain()
    {
        var service = NewService();
        await service.AddWordAsync(_alpha, "enthalpy");

        var changed = 0;
        service.OnDictionaryChanged += _ => changed++;

        service.WordsFor(_alpha);
        service.WordsFor(_alpha);

        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task AddingAWordDoesNotWriteBackOverAnOutsideEdit()
    {
        var service = NewService();
        await service.AddWordAsync(_alpha, "enthalpy");

        File.WriteAllLines(service.PathFor(_alpha), ["enthalpy", "exergy"]);
        await service.AddWordAsync(_alpha, "SOC");

        Assert.Equal(["enthalpy", "exergy", "SOC"], service.WordsFor(_alpha));
    }

    #endregion

    #region Import, export and the machine list

    [Fact]
    public async Task MergeFrom_AddsOnlyWhatIsNew()
    {
        var service = NewService();
        await service.AddWordAsync(_alpha, "enthalpy");

        var source = Path.Combine(_root, "incoming.txt");
        await File.WriteAllLinesAsync(source, ["enthalpy", "SOC", "exergy"]);

        var added = await service.MergeFromAsync(_alpha, source);

        Assert.Equal(2, added);
        Assert.Equal(["enthalpy", "exergy", "SOC"], service.WordsFor(_alpha));
    }

    [Fact]
    public async Task Export_WritesTheRepositorysWords()
    {
        var service = NewService();
        await service.AddWordAsync(_alpha, "enthalpy");

        var target = Path.Combine(_root, "out", "words.txt");
        await service.ExportAsync(_alpha, target);

        Assert.Equal(["enthalpy"], await File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task TheMachineList_IsOfferedForImportButNeverReadForChecking()
    {
        // Words accumulated before this moved are not lost, but they only apply once someone has
        // deliberately brought them into a repository — a list only the app can see is the problem
        // this change exists to remove.
        var legacy = Path.Combine(_root, "custom_dictionary.txt");
        await File.WriteAllLinesAsync(legacy, ["enthalpy", "exergy"]);

        var service = NewService(legacy);

        Assert.Equal(legacy, service.LegacyMachineDictionaryPath);
        Assert.Empty(service.WordsFor(_alpha));

        var added = await service.MergeFromAsync(_alpha, legacy);

        Assert.Equal(2, added);
        Assert.Equal(["enthalpy", "exergy"], service.WordsFor(_alpha));
    }

    [Fact]
    public void NoMachineList_IsReportedAsAbsent()
    {
        Assert.Null(NewService(Path.Combine(_root, "not-here.txt")).LegacyMachineDictionaryPath);
    }

    #endregion

    #region Encoding (B89)

    private const string NL = "\n";

    // The list is a committed file people hand-edit, and it holds engineering terms and proper nouns
    // that are not all ASCII. It declares no encoding, so it is read the way a .mo file is —
    // per file, from its bytes — rather than assumed to be UTF-8.

    [Fact]
    public void AWindows1252ListIsReadWithoutMangling()
    {
        var service = NewService();
        var path = service.PathFor(_alpha);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes("Frössling" + NL + "Krüger" + NL));

        Assert.Equal(["Frössling", "Krüger"], service.WordsFor(_alpha).OrderBy(w => w));
    }

    [Fact]
    public async Task AndIsWrittenBackInTheSameEncoding()
    {
        // The half that made it corruption rather than a display problem: reading as UTF-8 gave
        // replacement characters, and the next accepted word wrote those characters back.
        var service = NewService();
        var path = service.PathFor(_alpha);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes("Frössling" + NL));

        await service.AddWordAsync(_alpha, "Nusselt");

        var reread = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Assert.Contains("Frössling", reread);
        Assert.DoesNotContain("�", reread);   // the replacement character
    }

    [Fact]
    public async Task AUtf8ListRoundTrips()
    {
        var service = NewService();
        var path = service.PathFor(_alpha);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Frössling" + NL, new System.Text.UTF8Encoding(false));

        await service.AddWordAsync(_alpha, "Nusselt");

        Assert.Equal(["Frössling", "Nusselt"], service.WordsFor(_alpha).OrderBy(w => w));
        Assert.Contains("Frössling", File.ReadAllText(path, new System.Text.UTF8Encoding(false)));
    }

    #endregion
}
