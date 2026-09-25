using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using static MLQT.Services.LoggingService;

namespace MLQT.Services.Helpers;

/// <summary>
/// Splits one package held in a single <c>.mo</c> file into a directory with a file per class — the
/// fix for an <c>MLQT.Structure.SingleFilePackage</c> finding.
///
/// <para><b>Why this is not just "press Format All Files".</b> The full library save restructures
/// everything, which is the right thing when you mean it and an absurd one when a package arrived
/// from another tool this morning and the rest of the library is already laid out correctly:
/// thousands of files rewritten to correct one. This does the same transformation to one package and
/// touches nothing else.</para>
///
/// <para><b>What it acts on is what the finding reported.</b> The set of classes to move comes from
/// <see cref="SingleFilePackageAnalyzer.SplittableChildren"/>, the same method the rule uses to
/// decide whether to report at all — so a package the rule did not report cannot be split, and one
/// it did report moves exactly the classes the message named.</para>
/// </summary>
public static class PackageSplitter
{
    /// <param name="WrittenFiles">Everything written, including the new <c>package.order</c>.</param>
    /// <param name="RemovedFiles">The single file the package used to live in, once it is gone.</param>
    /// <param name="Error">Null on success; a sentence for the user otherwise.</param>
    public sealed record SplitResult(
        IReadOnlyList<string> WrittenFiles,
        IReadOnlyList<string> RemovedFiles,
        string? Error)
    {
        public bool Succeeded => Error is null;

        public static SplitResult Failed(string error) => new([], [], error);
    }

    /// <summary>
    /// Whether this package is one the fix can act on — the finding's own question, asked again at
    /// the point of acting because the graph may have moved on since the finding was raised.
    /// </summary>
    public static bool CanSplit(DirectedGraph graph, ModelNode package) =>
        SingleFilePackageAnalyzer.SplittableChildren(
            package,
            SingleFilePackageAnalyzer.ChildrenByParent(graph),
            graph.GetNode<ModelNode>).Count > 0;

    /// <summary>
    /// Writes <paramref name="package"/> as a directory beside the file it currently occupies, and
    /// deletes that file.
    ///
    /// <para>The save is the ordinary library save pointed at one file's classes: given the package
    /// and everything below it that is stored in the same file, <c>ModelicaPackageSaver</c> creates
    /// <c>&lt;parent&gt;/&lt;Name&gt;/package.mo</c> with a file per standalone child and a
    /// <c>package.order</c> to match. The <em>parent</em> package is not in the set and is not
    /// rewritten — its own <c>package.order</c> already names this package and still does, because
    /// what changes is where the package is stored and not what it is called.</para>
    ///
    /// <para>The old file is deleted last, and only when the save wrote the new one somewhere else.
    /// Deleting first would lose the package if the save then failed, and deleting a path the save
    /// happens to have written would delete the result.</para>
    ///
    /// <para><b>It formats exactly as the rest of MLQT would</b> (B305): <paramref name="settings"/>
    /// goes to the save, which moves a class verbatim when the repository does not format or when
    /// <see cref="FormattingExclusion.Excludes"/> names it — the name list as well as the
    /// annotation.</para>
    /// </summary>
    /// <param name="settings">The repository's settings. Null asks only the classes' own source.</param>
    public static SplitResult Split(
        DirectedGraph graph, ModelNode package, FormattingOptions formatting, StyleCheckingSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(package);

        if (!CanSplit(graph, package))
            return SplitResult.Failed(
                $"{package.Definition.Name} is not a package stored in a single file, or none of its "
                + "classes can be stored on their own.");

        var currentFile = graph.GetNode<FileNode>(package.ContainingFileId ?? "")?.FilePath;
        if (string.IsNullOrEmpty(currentFile))
            return SplitResult.Failed($"Cannot tell which file {package.Definition.Name} is stored in.");

        // Where the save is pointed, which is the directory the package's own folder sits in —
        // ModelicaPackageSaver creates that folder itself.
        //
        // <b>A package.mo is not a special case.</b> A package can be a directory and still hold its
        // classes inline, which the rule reports for the same reason and which this fixes the same
        // way: the children get files beside the package.mo and the package.mo is rewritten without
        // them. Refusing it, which this did at first, would have offered a fix on a finding and then
        // declined to apply it. The only difference is that the file the package lives in is one the
        // save rewrites, so there is nothing to delete afterwards.
        var isDirectoryPackage =
            string.Equals(Path.GetFileName(currentFile), "package.mo", StringComparison.OrdinalIgnoreCase);

        var packageFolder = Path.GetDirectoryName(currentFile);
        var parentDirectory = isDirectoryPackage ? Path.GetDirectoryName(packageFolder) : packageFolder;
        if (string.IsNullOrEmpty(parentDirectory))
            return SplitResult.Failed($"Cannot tell which directory {currentFile} is in.");

        var modelIds = StoredWith(graph, package);

        // What is on disk where the save writes, so a save that does not finish can be taken back
        // (B303). The save logs a failed write and carries on, and throws only for what it cannot
        // carry on from — either way what it leaves is a half-written package beside the file the
        // classes are still in, which the next load reads as every class defined twice.
        var packageDirectory = Path.Combine(parentDirectory, package.Definition.Name);
        var before = DiskSnapshot.Take(packageDirectory,
            isDirectoryPackage ? [currentFile, Path.Combine(packageDirectory, "package.order")] : []);

        // The files the split creates are written the way the one they came from was: the same
        // encoding and the same line endings, rather than UTF-8 LF beside a CRLF library (B308).
        var sourceStyle = ModelicaFileEncoding.StyleOf(currentFile);

        SaveResult saved;
        try
        {
            saved = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
                graph, modelIds, parentDirectory, showAnnotations: true, formatting: formatting,
                settings: settings, newFileStyle: sourceStyle);
        }
        catch (Exception ex)
        {
            Error(nameof(PackageSplitter), $"Failed to split {package.Id} into {parentDirectory}", ex);
            return Undone(before, package, $"Could not write the package: {ex.Message}.");
        }

        // Every class has to be somewhere written before the file it came from can go. The save's
        // own list of what it wrote is the only evidence of that: asking the graph which classes
        // were *asked* to move counted a class whose write failed as moved, and deleted the only
        // copy of it (B303).
        var unwritten = modelIds
            .Where(id => !saved.ModelIdToFilePath.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (unwritten.Count > 0 || saved.FailedFiles.Count > 0)
        {
            var what = unwritten.Count switch
            {
                0 => Path.GetFileName(saved.FailedFiles.First()),
                1 => unwritten[0],
                _ => $"{unwritten[0]} and {unwritten.Count - 1} other class(es)",
            };
            Error(nameof(PackageSplitter),
                $"Splitting {package.Id} wrote only part of it: {string.Join(", ", saved.FailedFiles)} "
                + $"failed, leaving {unwritten.Count} class(es) unwritten");
            return Undone(before, package, $"Could not write {what}.");
        }

        if (saved.WrittenFiles.Count == 0)
            return SplitResult.Failed($"Nothing was written for {package.Definition.Name}.");

        var removed = new List<string>();
        if (!saved.WrittenFiles.Any(f => PathsEqual(f, currentFile)))
        {
            // Nothing is deleted while it still holds something (B243). The package owns its file,
            // so everything in it should have moved — but "should" is what this checks, because the
            // cost of being wrong is a file of somebody else's classes deleted from their working
            // copy. That is what happened: a package nested inside another class's file was split,
            // the save wrote it somewhere else entirely, and this deleted the file it came from
            // along with the twenty-two other packages in it.
            var stranded = StillLivingIn(graph, currentFile, modelIds);
            if (stranded is not null)
            {
                Error(nameof(PackageSplitter),
                    $"Refusing to delete {currentFile} after splitting {package.Id}: it still holds {stranded}");
                return new SplitResult([.. saved.WrittenFiles], removed,
                    $"{package.Definition.Name} was written to its new directory, but "
                    + $"{Path.GetFileName(currentFile)} was left alone because it also holds "
                    + $"{stranded}. Nothing was deleted; remove the duplicate by hand once you have "
                    + "checked it.");
            }

            try
            {
                File.Delete(currentFile);
                removed.Add(currentFile);
            }
            catch (Exception ex)
            {
                // The package is now in both places. Saying so is better than a silent half-move:
                // the library would load the same classes twice.
                Error(nameof(PackageSplitter), $"Split {package.Id} but could not delete {currentFile}", ex);
                return new SplitResult([.. saved.WrittenFiles], removed,
                    $"The package was written to its new directory, but {Path.GetFileName(currentFile)} "
                    + $"could not be deleted ({ex.Message}). Delete it by hand — until you do, the "
                    + "library holds this package twice.");
            }
        }

        Info(nameof(PackageSplitter),
            $"Split {package.Id} into {saved.WrittenFiles.Count} file(s) under {parentDirectory}");

        return new SplitResult([.. saved.WrittenFiles], removed, Error: null);
    }

    /// <summary>
    /// A class still stored in <paramref name="filePath"/> that is not part of what was just
    /// written, described for a message — or null when the file holds nothing else.
    /// </summary>
    internal static string? StillLivingIn(DirectedGraph graph, string filePath, HashSet<string> moved)
    {
        var fileId = graph.FileNodes.FirstOrDefault(f => PathsEqual(f.FilePath, filePath))?.Id;
        if (string.IsNullOrEmpty(fileId))
            return null;

        // By the models' own ContainingFileId rather than the file node's contained-ids edge: it is
        // the same question asked of the side that is always set, and a guard that depends on an
        // edge being populated is a guard that can quietly answer "nothing here".
        var others = graph.ModelNodes
            .Where(m => string.Equals(m.ContainingFileId, fileId, StringComparison.Ordinal))
            .Where(m => !moved.Contains(m.Id))
            .Select(m => m.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (others.Count == 0)
            return null;

        return others.Count == 1
            ? others[0]
            : $"{others[0]} and {others.Count - 1} other class(es)";
    }

    /// <summary>
    /// The disk put back as it was before a split that did not finish, and a result saying so — or,
    /// when even that failed, saying exactly what was left behind.
    /// </summary>
    private static SplitResult Undone(DiskSnapshot before, ModelNode package, string cause)
    {
        var leftBehind = before.Restore();
        if (leftBehind.Count == 0)
            return SplitResult.Failed(
                $"{cause} The split of {package.Definition.Name} was undone and nothing was deleted; "
                + "its classes are where they were.");

        Error(nameof(PackageSplitter),
            $"Could not undo the partial split of {package.Id}; left on disk: {string.Join(", ", leftBehind)}");
        return new SplitResult(leftBehind, [],
            $"{cause} Nothing was deleted, so {package.Definition.Name} is still in its original file, "
            + $"but {leftBehind.Count} file(s) written for the split could not be removed again: "
            + $"{string.Join(", ", leftBehind.Take(3).Select(Path.GetFileName))}"
            + (leftBehind.Count > 3 ? ", ..." : "")
            + ". Delete them by hand — until you do, the library holds those classes twice.");
    }

    /// <summary>
    /// The state of the directory a split writes into, from before the split, and the means of
    /// putting it back.
    ///
    /// <para>Everything the save creates is under one directory — the package's own — so what it
    /// created is what is there afterwards and was not there before. The only files it rewrites
    /// in place are a directory package's own <c>package.mo</c> and <c>package.order</c>, and those
    /// are kept whole: a <c>package.mo</c> rewritten without its classes, when one of those classes
    /// then failed to reach its own file, is the class lost.</para>
    /// </summary>
    private sealed class DiskSnapshot
    {
        private readonly string _directory;
        private readonly bool _existed;
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _contents = new(StringComparer.OrdinalIgnoreCase);

        private DiskSnapshot(string directory)
        {
            _directory = directory;
            _existed = Directory.Exists(directory);
        }

        public static DiskSnapshot Take(string directory, IEnumerable<string> keep)
        {
            var snapshot = new DiskSnapshot(directory);
            if (snapshot._existed)
            {
                snapshot._files.UnionWith(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
                snapshot._directories.UnionWith(
                    Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories));
            }

            // Bytes, not text: this is put back exactly as it was, encoding and line endings included.
            foreach (var path in keep)
                if (File.Exists(path))
                    snapshot._contents[path] = File.ReadAllBytes(path);

            return snapshot;
        }

        /// <summary>Puts the directory back; returns whatever could not be.</summary>
        public List<string> Restore()
        {
            var failed = new List<string>();

            foreach (var (path, bytes) in _contents)
            {
                try { File.WriteAllBytes(path, bytes); }
                catch (Exception ex) { Keep(path, ex); }
            }

            if (!Directory.Exists(_directory))
                return failed;

            if (!_existed)
            {
                try { Directory.Delete(_directory, recursive: true); }
                catch (Exception ex) { Keep(_directory, ex); }
                return failed;
            }

            foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories).ToList())
            {
                if (_files.Contains(file))
                    continue;
                try { File.Delete(file); }
                catch (Exception ex) { Keep(file, ex); }
            }

            // Deepest first, and never recursively: only a directory the split created, and only
            // once it is empty again.
            foreach (var directory in Directory.EnumerateDirectories(_directory, "*", SearchOption.AllDirectories)
                         .Where(d => !_directories.Contains(d))
                         .OrderByDescending(d => d.Length)
                         .ToList())
            {
                try { Directory.Delete(directory); }
                catch (Exception ex) { Keep(directory, ex); }
            }

            return failed;

            void Keep(string path, Exception ex)
            {
                Warn(nameof(PackageSplitter), $"Could not undo {path}: {ex.Message}");
                failed.Add(path);
            }
        }
    }

    /// <summary>
    /// The package and every class beneath it that is stored in the package's own file, which is
    /// the set the save has to be given: a child left out would be written nowhere and lost with the
    /// old file.
    ///
    /// <para><b>Only that file's classes</b> (B305). A child of a directory package that already has
    /// a file of its own has nowhere to move to, and handing it to the save rewrote it anyway — a
    /// fix for one file reformatting the others around it. Its name stays in the rewritten
    /// <c>package.order</c> because the package's stored order is what that file is built from.</para>
    /// </summary>
    private static HashSet<string> StoredWith(DirectedGraph graph, ModelNode package)
    {
        var prefix = package.Id + ".";
        var fileId = package.ContainingFileId;
        var ids = new HashSet<string>(StringComparer.Ordinal) { package.Id };

        foreach (var model in graph.ModelNodes)
            if (model.Id.StartsWith(prefix, StringComparison.Ordinal)
                && string.Equals(model.ContainingFileId, fileId, StringComparison.Ordinal))
                ids.Add(model.Id);

        return ids;
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
