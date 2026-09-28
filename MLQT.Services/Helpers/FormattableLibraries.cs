using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Helpers;

/// <summary>
/// Which loaded libraries a full "format everything" pass may write to.
/// </summary>
/// <remarks>
/// <para>This is a <b>write guard</b>, which is why it is answerable on its own rather than left as
/// three <c>Where</c> clauses inside a hundred-line save. Two of the three exclusions protect a
/// library MLQT must never rewrite: an encrypted one holds nothing but MLQT's reconstruction of a
/// vendor's documentation, so a write would replace a `package.moe` with invented source; a
/// reference one is a tool's installed library or a vendor's copy, which the settings page promises
/// is never formatted. The third is the caller's own narrowing to one repository.</para>
///
/// <para>Lifted out of <see cref="FormattingPipeline"/> for B219. The whole-solution mutation audit
/// found that inverting either exclusion was something no test objected to — the save ran in tests
/// only against libraries that were never going to be excluded, so the clauses were executed on
/// every run and depended on by nothing.</para>
/// </remarks>
public static class FormattableLibraries
{
    /// <summary>
    /// The libraries a full save may write, in the order they were loaded.
    /// </summary>
    /// <param name="libraries">Every loaded library.</param>
    /// <param name="repositories">Used to decide whether a library is reference-only; one of the
    /// three answers <see cref="ReferenceOnlyScope"/> reconciles.</param>
    /// <param name="filterRepositoryId">Narrow to one repository, or null for all of them. Null is
    /// "every repository", not "every library" — the two exclusions above still apply, which they
    /// used to do only when this was non-null.</param>
    public static IReadOnlyList<LoadedLibrary> Select(
        IEnumerable<LoadedLibrary> libraries,
        IRepositoryService repositories,
        string? filterRepositoryId) =>
        libraries
            .Where(l => l.SourceType != LibrarySourceType.EncryptedDirectory)
            .Where(l => !ReferenceOnlyScope.IsReference(l, repositories))
            .Where(l => filterRepositoryId == null || l.RepositoryId == filterRepositoryId)
            .ToList();
}
