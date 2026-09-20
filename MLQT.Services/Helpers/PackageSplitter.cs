using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
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
            package, SingleFilePackageAnalyzer.ChildrenByParent(graph)).Count > 0;

    /// <summary>
    /// Writes <paramref name="package"/> as a directory beside the file it currently occupies, and
    /// deletes that file.
    ///
    /// <para>The save is the ordinary library save pointed at one subtree: given the package and
    /// everything below it, <c>ModelicaPackageSaver</c> creates
    /// <c>&lt;parent&gt;/&lt;Name&gt;/package.mo</c> with a file per standalone child and a
    /// <c>package.order</c> to match. The <em>parent</em> package is not in the set and is not
    /// rewritten — its own <c>package.order</c> already names this package and still does, because
    /// what changes is where the package is stored and not what it is called.</para>
    ///
    /// <para>The old file is deleted last, and only when the save wrote the new one somewhere else.
    /// Deleting first would lose the package if the save then failed, and deleting a path the save
    /// happens to have written would delete the result.</para>
    /// </summary>
    public static SplitResult Split(DirectedGraph graph, ModelNode package, FormattingOptions formatting)
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

        var modelIds = SubtreeOf(graph, package);

        SaveResult saved;
        try
        {
            saved = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
                graph, modelIds, parentDirectory, showAnnotations: true, formatting: formatting);
        }
        catch (Exception ex)
        {
            Error(nameof(PackageSplitter), $"Failed to split {package.Id} into {parentDirectory}", ex);
            return SplitResult.Failed($"Could not write the package: {ex.Message}");
        }

        if (saved.WrittenFiles.Count == 0)
            return SplitResult.Failed($"Nothing was written for {package.Definition.Name}.");

        var removed = new List<string>();
        if (!saved.WrittenFiles.Any(f => PathsEqual(f, currentFile)))
        {
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
    /// The package and every class beneath it, which is the set the save has to be given: a child
    /// left out would be written nowhere and lost with the old file.
    /// </summary>
    private static HashSet<string> SubtreeOf(DirectedGraph graph, ModelNode package)
    {
        var prefix = package.Id + ".";
        var ids = new HashSet<string>(StringComparer.Ordinal) { package.Id };

        foreach (var model in graph.ModelNodes)
            if (model.Id.StartsWith(prefix, StringComparison.Ordinal))
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
