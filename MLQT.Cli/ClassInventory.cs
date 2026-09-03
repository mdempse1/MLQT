using ModelicaGraph.DataTypes;
using MLQT.Services;

namespace MLQT.Cli;

/// <summary>
/// One class as the comparison sees it. <see cref="FullName"/> is the Modelica path
/// (<c>Modelica.Blocks.Continuous.PID</c>) — deliberately the only identity used, because it is the
/// one thing that does not change when a library is restructured on disk. Everything else here is
/// carried so a missing class can be pointed at in the library it went missing from.
/// </summary>
internal sealed record ClassEntry(
    string FullName,
    string SimpleName,
    string ClassType,
    string FilePath,
    int StartLine,
    bool IsParseFailure);

/// <summary>
/// Every class in a library path, keyed by full Modelica name.
///
/// <para>Loaded with the same discovery and parsing the rest of MLQT uses, so a class the app can see
/// is a class this can see. No style settings are read and no rules are run: a comparison only asks
/// what is there.</para>
/// </summary>
internal sealed class ClassInventory
{
    public required string Path { get; init; }

    /// <summary>Top-level library names found under the path, for the header line.</summary>
    public required IReadOnlyList<string> LibraryNames { get; init; }

    public required IReadOnlyDictionary<string, ClassEntry> Classes { get; init; }

    /// <summary>
    /// Files the parser could not get any class out of, relative to the library path. Reported
    /// prominently because every class such a file held is indistinguishable from a class that was
    /// deleted — which is exactly the wrong conclusion to draw about a file a formatter has just
    /// rewritten.
    /// </summary>
    public required IReadOnlyList<string> UnparseableFiles { get; init; }

    public int Count => Classes.Count;

    /// <summary>
    /// Whether the path is something that could hold a library, writing the reason if not.
    ///
    /// <para>Separate from <see cref="LoadAsync"/> so a comparison can reject a mistyped second path
    /// before spending several minutes loading the first one.</para>
    /// </summary>
    public static bool ValidatePath(string path, TextWriter stderr)
    {
        if (Directory.Exists(path) ||
            (File.Exists(path) && path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase)))
            return true;

        stderr.WriteLine($"error: library path not found: {path}");
        return false;
    }

    /// <summary>Loads a library path, or writes the reason it could not and returns null.</summary>
    public static async Task<ClassInventory?> LoadAsync(string path, TextWriter stderr)
    {
        if (!ValidatePath(path, stderr))
            return null;

        var isDirectory = Directory.Exists(path);
        var libraryPaths = LibraryDiscovery.DiscoverLibraryPaths(path);
        if (libraryPaths.Count == 0)
        {
            stderr.WriteLine(
                $"error: no Modelica libraries found under {path} " +
                "(expected a package.mo, sub-package directories, or .mo files)");
            return null;
        }

        // A service instance owns its own graph, so loading the two copies separately is what keeps
        // two libraries of the same name — the whole point of this command — from colliding on
        // identical class ids.
        var libraryData = new LibraryDataService();
        var libraryNames = new List<string>();
        foreach (var libraryPath in libraryPaths)
        {
            try
            {
                libraryNames.Add((await libraryData.AddLibraryFromPathAsync(libraryPath)).Name);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"warning: failed to load '{libraryPath}': {ex.Message}");
            }
        }

        var graph = libraryData.CombinedGraph;
        var root = isDirectory ? path : System.IO.Path.GetDirectoryName(path) ?? path;

        var modelToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        var unparseable = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in graph.FileNodes)
        {
            var relativePath = Relative(root, file.FilePath);
            var contributed = 0;
            foreach (var model in graph.GetModelsInFile(file.Id))
            {
                modelToFile[model.Id] = relativePath;
                contributed++;
            }

            // A .mo file always declares at least one class, so one that produced none was not read.
            // Saying so matters more here than anywhere else: every class such a file held looks
            // exactly like a class that was deleted, and a bulk edit is the very thing most likely to
            // have left a file the parser cannot get through.
            if (contributed == 0)
                unparseable.Add(relativePath);
        }

        var classes = new Dictionary<string, ClassEntry>(StringComparer.Ordinal);
        foreach (var model in graph.ModelNodes)
        {
            var file = modelToFile.GetValueOrDefault(model.Id, string.Empty);
            if (model.IsParseFailurePlaceholder && file.Length > 0)
                unparseable.Add(file);

            classes[model.Id] = new ClassEntry(
                model.Id, model.Name, model.ClassType, file, model.StartLine,
                model.IsParseFailurePlaceholder);
        }

        return new ClassInventory
        {
            Path = path,
            LibraryNames = libraryNames,
            Classes = classes,
            UnparseableFiles = [.. unparseable]
        };
    }

    private static string Relative(string root, string filePath)
    {
        try
        {
            return System.IO.Path.GetRelativePath(root, filePath).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            // Different volumes, or a path the platform will not relativise — the absolute path
            // still identifies the file, which is all this is for.
            return filePath.Replace('\\', '/');
        }
    }
}
