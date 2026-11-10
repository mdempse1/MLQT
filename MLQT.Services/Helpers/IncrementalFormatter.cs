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
    public static Task<IReadOnlyDictionary<string, DateTime>> FormatAndWriteAsync(
        DirectedGraph graph,
        IEnumerable<string> changedFilePaths,
        StyleCheckingSettings settings,
        Func<string, bool>? neverWritten = null)
        => FormatAndWriteAsync(graph, changedFilePaths, settings, neverWritten, classesMatchFiles: false);

    /// <param name="classesMatchFiles">
    /// True when the caller knows the graph's classes were read from these files as they are now - a
    /// format straight after loading, at startup or on a project switch. A file already formatted then
    /// needs nothing at all: re-reading its classes from the same text gives back what they hold. That
    /// re-read parses the file, one file after another, and at a startup with 17,372 changed files
    /// already formatted it was 35 seconds spent changing nothing. False keeps it, for a caller whose
    /// graph may lag a file the user has since edited - the format before a commit.
    /// </param>
    /// <param name="alreadyFormatted">
    /// Given each file found already formatted, with its write time from before it was read, so a
    /// later format can skip it while that time stands. Without it every pass rendered every changed
    /// file again: a startup found 17,482 changed files already formatted, and the format before the
    /// commit two minutes later rendered all of them once more, 51 seconds before the dialog opened.
    /// </param>
    internal static async Task<IReadOnlyDictionary<string, DateTime>> FormatAndWriteAsync(
        DirectedGraph graph,
        IEnumerable<string> changedFilePaths,
        StyleCheckingSettings settings,
        Func<string, bool>? neverWritten,
        bool classesMatchFiles,
        IDictionary<string, DateTime>? alreadyFormatted = null)
    {
        var written = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        var filesToProcess = SelectFilesToFormat(graph, changedFilePaths, settings, File.Exists, neverWritten);
        if (filesToProcess.Count == 0)
            return written;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var large = filesToProcess.Count >= LargeFormat;

        // Captured once: the parallel body below must not read settings that another thread may edit.
        var formatting = settings.ToFormattingOptions();
        var formattedFiles = new ConcurrentDictionary<string, (FileToFormat Entry, string Text, bool Unchanged, bool AsLoaded, DateTime ReadAt)>();

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
                //
                // The write time is taken before the read: an edit landing between the two leaves
                // the file newer than the time recorded, so it is never taken for formatted.
                var readAt = File.GetLastWriteTimeUtc(fileEntry.FilePath);
                var fileSource = ModelicaFileEncoding.ReadAllTextOnly(fileEntry.FilePath);

                // Whether the graph's classes are still this file's text: its write time is the one
                // the load recorded, and no class in it has been changed in the graph since. Asked
                // after the read, so an edit landing between the two reads as newer, never as loaded.
                var asLoaded = classesMatchFiles
                    && graph.GetNode<FileNode>(GraphBuilder.GenerateFileId(fileEntry.FilePath))?.LoadedWriteTimeUtc
                        is { } loadedAt
                    && File.GetLastWriteTimeUtc(fileEntry.FilePath) == loadedAt
                    && fileEntry.Models.All(m => m.SourceMatchesFile);

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
                //
                // **Already formatted, then not written.** ForFile is what the write would put in the
                // file - its line endings and final newline - and the encoding is kept, so the same
                // text is the same file. Every startup formats every changed file, and with 17,372 of
                // them, already formatted by the last startup, each one was written again: a write
                // apiece, and a new write time that made every cached change classification stale.
                // From the text already read, not by opening the file again (ForFileText).
                var unchanged = ModelicaFileEncoding.ForFileText(fileSource, formatted) == fileSource;
                formattedFiles[fileEntry.FilePath] = (fileEntry, formatted, unchanged, asLoaded, readAt);
            }
            catch (Exception ex)
            {
                Warn(nameof(IncrementalFormatter), $"Failed to format file {fileEntry.FilePath}: {ex.Message}");
            }
        }));

        var renderedMs = stopwatch.ElapsedMilliseconds;
        if (large)
            Info(nameof(IncrementalFormatter),
                $"Rendered {formattedFiles.Count} of {filesToProcess.Count} file(s) in {renderedMs}ms; writing them");

        // Written one at a time: these are the user's files, and a partial parallel write is worse
        // than a slow one.
        //
        // **No line per file.** There was one, and a startup with 17,372 changed files wrote 17,372
        // of them - most of the day's log - while saying nothing about how far it had got. Progress
        // every so many files, and the total, say more.
        var alreadyFormattedCount = 0;
        var done = 0;
        foreach (var (filePath, (entry, content, unchanged, asLoaded, readAt)) in formattedFiles)
        {
            if (large && ++done % ProgressEvery == 0)
                Info(nameof(IncrementalFormatter), $"Saved {done}/{formattedFiles.Count} formatted file(s)");

            if (unchanged)
            {
                // Nothing to write - and nothing to read back either, when the classes are this very
                // text: the caller says the graph was just loaded, and the file is as it was loaded.
                // Otherwise they are brought up to date from it, as a write would: the graph may not
                // have caught up with an edit the file already has - one made in another editor
                // during a startup, before the monitor was watching, say.
                alreadyFormattedCount++;
                if (!asLoaded)
                    RefreshClassesFromWrittenFile(graph, entry, content);
                if (alreadyFormatted is not null)
                    alreadyFormatted[filePath] = readAt;
                continue;
            }

            try
            {
                await ModelicaFileEncoding.WriteAllTextAsync(filePath, content);
                written[filePath] = File.GetLastWriteTimeUtc(filePath);
            }
            catch (Exception ex)
            {
                Warn(nameof(IncrementalFormatter), $"Failed to save formatted file {filePath}: {ex.Message}");
                continue;
            }

            // Only once the file holds it: a file that could not be written is still on disk as it
            // was, and so are its classes (B374's rule, on this path).
            RefreshClassesFromWrittenFile(graph, entry, content);

            // The classes are now the file as written, which is what the recorded time stands for.
            if (graph.GetNode<FileNode>(GraphBuilder.GenerateFileId(filePath)) is { } fileNode)
                fileNode.LoadedWriteTimeUtc = written[filePath];
        }

        var summary = $"Wrote {written.Count} formatted file(s) of {filesToProcess.Count} selected, "
            + $"{alreadyFormattedCount} already formatted ({filesToProcess.Count - written.Count - alreadyFormattedCount} "
            + $"not formatted); rendering {renderedMs}ms, saving {stopwatch.ElapsedMilliseconds - renderedMs}ms";
        if (large)
            Info(nameof(IncrementalFormatter), summary);
        else
            Debug(nameof(IncrementalFormatter), summary);

        return written;
    }

    /// <summary>A format of at least this many files reports its progress at Info.</summary>
    internal const int LargeFormat = 100;

    /// <summary>How often a large format reports its progress, in files written.</summary>
    internal const int ProgressEvery = 1000;

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
        var retrim = new HashSet<string>(StringComparer.Ordinal);
        RefreshClassesFromFileText(entry.FilePath, entry.Models, content, retrim.Add);

        if (retrim.Count > 0)
            PackageCodeTrimmer.TrimStandaloneChildren(graph, retrim);
    }

    /// <summary>
    /// The classes of a file just written, taken from its text: each one's stored source becomes its
    /// verbatim slice of <paramref name="content"/>, and its lines and offsets where that text has
    /// them, so <c>ClassLocation</c> maps a finding to the line it is on. Shared by both formatters -
    /// Format All stored the rendered text and gave up the mapping instead, so every finding after it
    /// was reported at its class's first line until the project was reloaded.
    /// </summary>
    /// <param name="filePath">The file, for the log.</param>
    /// <param name="models">Every class the graph stores in the file.</param>
    /// <param name="content">The file's text as written.</param>
    /// <param name="retrim">
    /// Given each package that had been trimmed, for the caller to trim again from its new source once
    /// every file is done (<see cref="PackageCodeTrimmer"/>). Touches only <paramref name="models"/>,
    /// so files can be refreshed in parallel.
    /// </param>
    internal static void RefreshClassesFromFileText(
        string filePath, IEnumerable<ModelNode> models, string content, Func<string, bool> retrim)
    {
        List<ModelInfo> extracted;
        try
        {
            (extracted, _) = ModelicaParserHelper.ExtractModelsWithErrors(
                ModelicaParserHelper.NormalizeLineEndings(content));
        }
        catch (Exception ex)
        {
            Warn(nameof(IncrementalFormatter), $"Could not re-read the classes of {filePath}: {ex.Message}");
            extracted = new();
        }

        var byId = new Dictionary<string, ModelInfo>(StringComparer.Ordinal);
        foreach (var info in extracted)
            byId.TryAdd(GraphBuilder.GenerateModelId(info.ParentModelName, info.Name), info);

        foreach (var model in models)
        {
            if (!byId.TryGetValue(model.Id, out var info))
            {
                // Not expected - the file was rendered from a parse that found it - but if the text
                // no longer has the class where the load did, no line in it can be trusted to map.
                Warn(nameof(IncrementalFormatter), $"{model.Id} not found in the formatted {filePath}");
                model.SourceMatchesFile = false;
                continue;
            }

            if (model.ChildrenTrimmed)
                retrim(model.Id);

            model.Definition.ModelicaCode = info.SourceCode;
            model.StartLine = info.StartLine;
            model.StopLine = info.StopLine;
            model.StartIndex = info.StartIndex;
            model.StopIndex = info.StopIndex;
            model.TrimElision = null;
            model.ChildrenTrimmed = false;
            model.SourceMatchesFile = true;
        }
    }
}
