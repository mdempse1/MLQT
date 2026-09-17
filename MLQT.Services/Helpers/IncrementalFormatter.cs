using System.Collections.Concurrent;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using static MLQT.Services.LoggingService;

namespace MLQT.Services.Helpers;

/// <summary>One file the incremental formatter will rewrite, and the classes it holds.</summary>
/// <param name="FilePath">The file on disk.</param>
/// <param name="Models">Every class stored in it, whose stored source is refreshed after rendering.</param>
/// <param name="Owner">
/// The topmost class in the file — the only one whose <c>within</c> clause describes the file.
/// </param>
public sealed record FileToFormat(string FilePath, IReadOnlyList<ModelNode> Models, ModelNode Owner);

/// <summary>
/// Reformats the files a change touched, in place.
/// </summary>
/// <remarks>
/// <para>The incremental path: it runs at startup over the VCS-modified files and again after every
/// VCS operation, which makes it the formatter most users meet. Its counterpart is the full library
/// save behind <b>Format All Files</b>.</para>
///
/// <para>Lifted out of <c>MainLayout</c> in phase 7a-4. It carries no UI — it was already only file
/// and graph work — and it is where <b>B65</b> lived: it asked the name list alone about formatting
/// exclusions, so <c>__MLQT(format=false)</c>, the rename-safe successor the documentation steers
/// people to, was honoured by Format All Files and ignored here. The class it was written on was
/// reordered in the working copy on the next pull.</para>
/// </remarks>
public static class IncrementalFormatter
{
    /// <summary>
    /// Which of the changed files will actually be rewritten, and by what.
    /// </summary>
    /// <remarks>
    /// Separated from the rendering so the exclusions can be asked about directly. Every one of them
    /// is a reason not to touch a user's file, and the consequence of losing one is a file rewritten
    /// that should not have been.
    /// </remarks>
    /// <param name="fileExists">Injected so the selection can be exercised without files on disk.</param>
    public static List<FileToFormat> SelectFilesToFormat(
        DirectedGraph graph,
        IEnumerable<string> changedFilePaths,
        StyleCheckingSettings settings,
        Func<string, bool> fileExists)
    {
        var selected = new List<FileToFormat>();
        if (!settings.ApplyFormattingRules)
            return selected;

        foreach (var filePath in changedFilePaths)
        {
            if (!fileExists(filePath))
                continue;

            // .git and .svn hold Modelica-looking files of their own; rewriting one corrupts the
            // working copy.
            if (FileMonitoringServiceHelpers.IsInHiddenDirectory(filePath))
            {
                Warn(nameof(IncrementalFormatter), $"Skipping file in hidden directory: {filePath}");
                continue;
            }

            var fileId = GraphBuilder.GenerateFileId(filePath);
            if (graph.GetNode<FileNode>(fileId) is null)
                continue;

            var modelNodes = graph.GetModelsInFile(fileId).ToList();
            if (modelNodes.Count == 0)
                continue;

            // A file is reformatted whole or not at all: the renderer works on the file's parse tree,
            // so there is no way to reformat some of its classes and leave a nested sibling untouched.
            // Excluding the whole file is the safe reading — never reformat a class the user opted out.
            //
            // Both mechanisms, through the one method that knows about both (B65).
            if (modelNodes.Any(m => FormattingExclusion.Excludes(m, settings)))
            {
                Debug(nameof(IncrementalFormatter), $"Skipping {filePath}: it holds a model excluded from formatting");
                continue;
            }

            // The owner is the topmost class stored in the file: it has no parent, or its parent
            // lives in another file. Only its within clause describes the file.
            var owner = modelNodes.FirstOrDefault(m =>
                string.IsNullOrEmpty(m.ParentModelName)
                || graph.GetNode<ModelNode>(m.ParentModelName)?.ContainingFileId != fileId);
            if (owner is null)
                continue;

            selected.Add(new FileToFormat(filePath, modelNodes, owner));
        }

        return selected;
    }

    /// <summary>
    /// Reformats and rewrites the changed files, and brings each class's stored source up to date.
    /// </summary>
    /// <returns>Each file written, with the write time recorded against it.</returns>
    public static async Task<IReadOnlyDictionary<string, DateTime>> FormatAndWriteAsync(
        DirectedGraph graph,
        IEnumerable<string> changedFilePaths,
        StyleCheckingSettings settings)
    {
        var written = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        var filesToProcess = SelectFilesToFormat(graph, changedFilePaths, settings, File.Exists);
        if (filesToProcess.Count == 0)
            return written;

        // Captured once: the parallel body below must not read settings that another thread may edit.
        var formatting = settings.ToFormattingOptions();
        var formattedFiles = new ConcurrentDictionary<string, string>();

        await Task.Run(() => Parallel.ForEach(filesToProcess, fileEntry =>
        {
            try
            {
                // Rendered from the file's own text on disk, never by concatenating the stored code of
                // the classes it holds. A class's ModelicaCode is not a file slice: a package has had
                // its inline standalone children trimmed out of it (PackageCodeTrimmer), nested classes
                // have their own nodes but live inside their parent's source, and a class that has been
                // through a full library save carries a within clause while a freshly loaded one does
                // not. Reassembling from those pieces duplicated the within clause and re-emitted every
                // nested class as a top-level sibling.
                var fileSource = ModelicaFileEncoding.ReadAllTextOnly(fileEntry.FilePath);

                var formatted = ModelicaPackageSaver.RenderFileSource(
                    fileSource, fileEntry.Owner.ParentModelName, formatting, out var parserErrors);

                // Reformatting invalid Modelica produces unreliable output, and this overwrites the
                // file in place. Leave a file we cannot parse exactly as the user left it — the style
                // check reports the syntax error, which is the actionable result.
                if (parserErrors.Count > 0)
                {
                    Warn(nameof(IncrementalFormatter),
                        $"Not formatting {fileEntry.FilePath}: {parserErrors.Count} syntax error(s), "
                        + $"first at line {parserErrors[0].Line}: {parserErrors[0].Message}");
                    return;
                }

                formattedFiles[fileEntry.FilePath] = formatted.TrimEnd() + "\n";

                // Bring each class's stored code up to date with what is about to be written, so style
                // checking and the code viewer see the formatted source without waiting for a reload.
                // Stored without a within clause, which is the representation the rest of the graph
                // expects — keeping one would shift every finding's line number by one.
                foreach (var modelNode in fileEntry.Models)
                {
                    var modelTree = ModelicaParserHelper.Parse(modelNode.Definition.ModelicaCode);
                    var visitor = new ModelicaRenderer(
                        renderForCodeEditor: false,
                        showAnnotations: true,
                        excludeClassDefinitions: false,
                        tokenStream: null,
                        classNamesToExclude: null,
                        formatting: formatting);
                    visitor.Visit(modelTree);
                    modelNode.Definition.ModelicaCode = WithinClause.Strip(string.Join("\n", visitor.Code));
                }
            }
            catch (Exception ex)
            {
                Warn(nameof(IncrementalFormatter), $"Failed to format file {fileEntry.FilePath}: {ex.Message}");
            }
        }));

        // Written one at a time: these are the user's files, and a partial parallel write is worse
        // than a slow one.
        foreach (var (filePath, content) in formattedFiles)
        {
            try
            {
                await ModelicaFileEncoding.WriteAllTextAsync(filePath, content);
                written[filePath] = File.GetLastWriteTimeUtc(filePath);
                Debug(nameof(IncrementalFormatter), $"Saved formatted file: {filePath}");
            }
            catch (Exception ex)
            {
                Warn(nameof(IncrementalFormatter), $"Failed to save formatted file {filePath}: {ex.Message}");
            }
        }

        return written;
    }
}
