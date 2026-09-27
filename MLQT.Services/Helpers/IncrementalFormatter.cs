using System.Collections.Concurrent;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser;
using ModelicaParser.Helpers;
using ModelicaParser.DataTypes;
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
    /// <param name="neverWritten">
    /// Whether a class must never be written, by its id - a class of a reference-only library
    /// (<see cref="MLQT.Services.Checking.ReferenceOnlyScope.OwnedByReference"/>). A file holding one
    /// is not touched. Asked here, of each class, because the files arrive by path and the settings
    /// they arrive with are the caller's guess at whose they are: a vendor library checked out inside
    /// a maintained repository's folder arrives as the outer repository's change (B421).
    /// </param>
    public static List<FileToFormat> SelectFilesToFormat(
        DirectedGraph graph,
        IEnumerable<string> changedFilePaths,
        StyleCheckingSettings settings,
        Func<string, bool> fileExists,
        Func<string, bool>? neverWritten = null)
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

            if (neverWritten is not null && modelNodes.Any(m => neverWritten(m.Id)))
            {
                Debug(nameof(IncrementalFormatter), $"Skipping {filePath}: it belongs to a reference-only library");
                continue;
            }

            var owner = FileOwner(graph, fileId, modelNodes);
            if (owner is null)
                continue;

            selected.Add(new FileToFormat(filePath, modelNodes, owner));
        }

        return selected;
    }

    /// <summary>
    /// The topmost class stored in a file: it has no parent, or its parent lives in another file.
    /// Only its within clause describes the file. Null when no class in the file qualifies.
    /// </summary>
    public static ModelNode? FileOwner(DirectedGraph graph, string fileId, IEnumerable<ModelNode> modelsInFile)
        => modelsInFile.FirstOrDefault(m =>
            string.IsNullOrEmpty(m.ParentModelName)
            || graph.GetNode<ModelNode>(m.ParentModelName)?.ContainingFileId != fileId);

    /// <summary>
    /// Reformats and rewrites the changed files, and brings each class's stored source up to date.
    /// </summary>
    /// <param name="neverWritten">See <see cref="SelectFilesToFormat"/>.</param>
    /// <returns>Each file written, with the write time recorded against it.</returns>
    public static async Task<IReadOnlyDictionary<string, DateTime>> FormatAndWriteAsync(
        DirectedGraph graph,
        IEnumerable<string> changedFilePaths,
        StyleCheckingSettings settings,
        Func<string, bool>? neverWritten = null)
    {
        var written = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        var filesToProcess = SelectFilesToFormat(graph, changedFilePaths, settings, File.Exists, neverWritten);
        if (filesToProcess.Count == 0)
            return written;

        // Captured once: the parallel body below must not read settings that another thread may edit.
        var formatting = settings.ToFormattingOptions();
        var formattedFiles = new ConcurrentDictionary<string, (FileToFormat Entry, string Text)>();

        // The same lookup the checker is given, for the same reason: the order written here and the
        // order MLQT.Style.DeclarationOrder asks for have to be one answer. Built only when the
        // layout asks for it — resolving a type is not free.
        var isSimpleType = formatting.DeclarationOrder ? StyleChecking.CreateSimpleTypeLookup(graph) : null;

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
                    fileSource, fileEntry.Owner.ParentModelName, formatting, out var parserErrors,
                    rootClassId: fileEntry.Owner.Id, isSimpleType: isSimpleType);

                // Reformatting invalid Modelica produces unreliable output, and this overwrites the
                // file in place. Leave a file we cannot parse exactly as the user left it — the style
                // check reports the syntax error, which is the actionable result. Format All asks the
                // same question of the same parse (ModelicaPackageSaver.SyntaxErrorsInFile, B414).
                if (parserErrors.Count > 0)
                {
                    Warn(nameof(IncrementalFormatter),
                        $"Not formatting {fileEntry.FilePath}: {ModelicaPackageSaver.DescribeSyntaxErrors(parserErrors)}");
                    return;
                }

                // How the file ends is ModelicaFileEncoding.EnsureFinalNewline's answer, applied by
                // the write below. This path used to append "\n" itself, which is how it and the
                // full library save came to disagree (B236).
                formattedFiles[fileEntry.FilePath] = (fileEntry, formatted);
            }
            catch (Exception ex)
            {
                Warn(nameof(IncrementalFormatter), $"Failed to format file {fileEntry.FilePath}: {ex.Message}");
            }
        }));

        // Written one at a time: these are the user's files, and a partial parallel write is worse
        // than a slow one.
        foreach (var (filePath, (entry, content)) in formattedFiles)
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
                continue;
            }

            // Only once the file holds it: a file that could not be written is still on disk as it
            // was, and so are its classes (B374's rule, on this path).
            RefreshClassesFromWrittenFile(graph, entry, content);
        }

        return written;
    }

    /// <summary>
    /// Brings every class in a file just written up to date with it, the way a fresh load of that
    /// file would: its stored source is the verbatim slice of the written text, and its lines and
    /// offsets are where that text has it (B444).
    /// </summary>
    /// <remarks>
    /// <para>This used to re-render each class's stored code on its own and store that, leaving
    /// <see cref="ModelNode.StartLine"/>, the offsets and <see cref="ModelNode.TrimElision"/> where
    /// the load had found them while <see cref="ModelNode.SourceMatchesFile"/> still said the text
    /// was the file's. A class below one the format had lengthened was mapped - in Findings, SARIF
    /// and every CLI report, through <c>ClassLocation</c> - to a line of the file as it used to be,
    /// and a trimmed package's findings through an elision of the old text. Taking the classes from
    /// the written text keeps the exact mapping, rather than giving it up the way a re-rendered class
    /// has to, because here the whole file was written and is known.</para>
    ///
    /// <para>A package that had been trimmed is trimmed again from its new source, so it stays the
    /// representation every surface checks (<see cref="PackageCodeTrimmer"/>).</para>
    /// </remarks>
    private static void RefreshClassesFromWrittenFile(DirectedGraph graph, FileToFormat entry, string content)
    {
        List<ModelInfo> extracted;
        try
        {
            (extracted, _) = ModelicaParserHelper.ExtractModelsWithErrors(
                ModelicaParserHelper.NormalizeLineEndings(content));
        }
        catch (Exception ex)
        {
            Warn(nameof(IncrementalFormatter), $"Could not re-read the classes of {entry.FilePath}: {ex.Message}");
            extracted = new();
        }

        var byId = new Dictionary<string, ModelInfo>(StringComparer.Ordinal);
        foreach (var info in extracted)
            byId.TryAdd(GraphBuilder.GenerateModelId(info.ParentModelName, info.Name), info);

        var retrim = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in entry.Models)
        {
            if (!byId.TryGetValue(model.Id, out var info))
            {
                // Not expected - the file was rendered from a parse that found it - but if the text
                // no longer has the class where the load did, no line in it can be trusted to map.
                Warn(nameof(IncrementalFormatter), $"{model.Id} not found in the formatted {entry.FilePath}");
                model.SourceMatchesFile = false;
                continue;
            }

            if (model.ChildrenTrimmed)
                retrim.Add(model.Id);

            model.Definition.ModelicaCode = info.SourceCode;
            model.StartLine = info.StartLine;
            model.StopLine = info.StopLine;
            model.StartIndex = info.StartIndex;
            model.StopIndex = info.StopIndex;
            model.TrimElision = null;
            model.ChildrenTrimmed = false;
            model.SourceMatchesFile = true;
        }

        if (retrim.Count > 0)
            PackageCodeTrimmer.TrimStandaloneChildren(graph, retrim);
    }
}
