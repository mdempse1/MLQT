using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <inheritdoc cref="IReferenceLibraryLoader"/>
/// <remarks>
/// <para>Failures are logged and stepped over rather than allowed to stop the caller: a reference
/// library is an aid to resolution, and a missing or unreadable one should degrade the check, not
/// prevent the application from opening the user's own work.</para>
/// </remarks>
public sealed class ReferenceLibraryLoader(ILibraryDataService libraries) : IReferenceLibraryLoader
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <inheritdoc/>
    public async Task<ReferenceLibraryLoadSummary> LoadAsync(
        IReadOnlyList<string> folders, bool useEncryptedDocumentation)
    {
        var missing = new List<string>();
        var loaded = 0;
        var classes = 0;
        var skipped = 0;

        if (folders.Count == 0)
            return new ReferenceLibraryLoadSummary();

        LogProcessStart("ReferenceLibraryLoader", "Loading reference libraries");

        // One announcement to the tree for the lot, rather than one per library.
        using var treeNotifications = libraries.SuppressTreeDataChanged();

        foreach (var configuredPath in folders)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) || !Directory.Exists(configuredPath))
            {
                Warn("ReferenceLibraryLoader", $"Reference library path not found: {configuredPath}");
                missing.Add(configuredPath);
                continue;
            }

            foreach (var libraryPath in LibraryDiscovery.DiscoverLibraryPaths(configuredPath))
            {
                try
                {
                    // Every reason not to load this one, in one place - see ReferenceLibraryRules. The
                    // name is read from the library, not taken from its path: ReferenceLibraryRules.
                    // CandidateName says why it has to be.
                    var isEncrypted = EncryptedLibraryDetector.IsEncryptedLibraryRoot(libraryPath);
                    var skip = ReferenceLibraryRules.ReasonToSkip(
                        libraryPath,
                        isEncrypted,
                        ReferenceLibraryRules.CandidateName(libraryPath, isEncrypted),
                        libraries.Libraries,
                        useEncryptedDocumentation);

                    if (skip is not null)
                    {
                        Info("ReferenceLibraryLoader", $"Skipping reference library at {libraryPath}: {skip}");
                        skipped++;
                        continue;
                    }

                    var library = await libraries.AddLibraryFromPathAsync(libraryPath);

                    // The one place that knows this library is reference material. It has no
                    // repository to carry the fact and - if it is readable rather than encrypted - no
                    // stub nodes either, so without recording it here every consumer had to re-derive
                    // it, and UnloadFolder could not tell it from a library the user added.
                    library.IsReferenceOnly = true;

                    if (library.ModelIds.Count == 0)
                    {
                        // An encrypted library with no documentation contributes nothing. Removing it
                        // keeps it out of the tree rather than showing an empty node that suggests the
                        // library is loaded and simply has no classes.
                        libraries.RemoveLibrary(library.Id);
                        continue;
                    }

                    loaded++;
                    classes += library.ModelIds.Count;
                }
                catch (Exception ex)
                {
                    Warn("ReferenceLibraryLoader", $"Failed to load reference library '{libraryPath}': {ex.Message}");
                }
            }
        }

        Info("ReferenceLibraryLoader",
            $"Loaded {loaded} reference libraries ({classes} classes) for reference resolution" +
            (skipped > 0 ? $"; skipped {skipped} already loaded" : ""));
        LogProcessEnd("ReferenceLibraryLoader", "Loading reference libraries");

        return new ReferenceLibraryLoadSummary
        {
            Loaded = loaded, Classes = classes, Skipped = skipped, MissingFolders = missing
        };
    }

    /// <inheritdoc/>
    public int UnloadFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return 0;

        var root = Normalize(folder);
        var unloaded = libraries.Libraries
            .Where(l => l.IsReferenceOnly && l.RepositoryId is null && IsAtOrBelow(l.SourcePath, root))
            .ToList();

        if (unloaded.Count == 0)
            return 0;

        using (libraries.SuppressTreeDataChanged())
        {
            foreach (var library in unloaded)
            {
                Info("ReferenceLibraryLoader", $"Unloading reference library '{library.Name}' at {library.SourcePath}");
                libraries.RemoveLibrary(library.Id);
            }
        }

        // The edges were built with these classes in the graph: references into them resolved, and
        // the classes that made them now point at nothing. The next consumer that needs the edges
        // must build them again rather than trust ones made against a different set of libraries.
        libraries.CombinedGraph.InvalidateDependencyAnalysis();
        return unloaded.Count;
    }

    private static bool IsAtOrBelow(string path, string root)
    {
        var normalized = Normalize(path);
        return string.Equals(normalized, root, PathComparison)
               || normalized.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)
               || normalized.StartsWith(root + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static string Normalize(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception) { return path; }
    }
}
