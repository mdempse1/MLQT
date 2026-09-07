namespace MLQT.Services.Interfaces;

/// <summary>
/// Manages each repository's custom spell-checking word list, stored at
/// <c>.mlqt/dictionary.txt</c> beside its settings.
///
/// <para><b>Why per repository.</b> The words a team has accepted are a property of the library, not
/// of whoever happens to be checking it: "SOC", "enthalpy", a customer's product name. Keeping them
/// on the machine meant the desktop app silently knew words a CI runner did not, so the same library
/// produced different spelling findings depending on where it was checked — with no way to tell from
/// either result that the two had been given different inputs. Committing the list with the code is
/// what makes the two provably identical.</para>
///
/// <para>The cost, accepted deliberately: a word that applies to several repositories has to be added
/// to each of them. There is no machine-wide layer to fall back on, because a fallback that only one
/// of the two tools can see is the problem, not the cure.</para>
/// </summary>
public interface ICustomDictionaryService
{
    /// <summary>
    /// The words accepted for <paramref name="repositoryRoot"/>, read from disk on first use and
    /// cached. Empty for a null or unknown root — a class outside any repository has no word list,
    /// and inventing one would put words somewhere neither tool would look again.
    /// </summary>
    IReadOnlyCollection<string> WordsFor(string? repositoryRoot);

    /// <summary>Adds a word to a repository's list and persists it.</summary>
    Task AddWordAsync(string repositoryRoot, string word);

    /// <summary>Removes a word from a repository's list and persists it.</summary>
    Task RemoveWordAsync(string repositoryRoot, string word);

    /// <summary>
    /// Reads a repository's list from disk, discarding anything cached. Safe to call when the file
    /// does not exist — the repository simply has no accepted words yet.
    /// </summary>
    Task<IReadOnlyCollection<string>> LoadAsync(string repositoryRoot);

    /// <summary>
    /// Merges the words in <paramref name="sourceFile"/> into a repository's list, returning how many
    /// were new. Backs both the settings page's import and the one-time migration of the old
    /// machine-wide list.
    /// </summary>
    Task<int> MergeFromAsync(string repositoryRoot, string sourceFile);

    /// <summary>Writes a repository's list to a file.</summary>
    Task ExportAsync(string repositoryRoot, string targetFile);

    /// <summary>
    /// The old machine-wide word list, or null when there is none. Offered for import in repository
    /// settings so words accumulated before this moved are not simply lost; nothing reads it while
    /// checking.
    /// </summary>
    string? LegacyMachineDictionaryPath { get; }

    /// <summary>Where a repository's word list lives.</summary>
    string PathFor(string repositoryRoot);

    /// <summary>Fired when a repository's list changes, carrying that repository's root.</summary>
    event Action<string>? OnDictionaryChanged;
}
