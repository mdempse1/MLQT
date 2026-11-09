using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Passes the file an edit is about to write through the formatter, with the rules of the repository
/// the class belongs to, so what an edit writes is already formatted and the agent need not call
/// mlqt_format after it. Inserted code was written in a compact form of the tool's own - a component
/// on one line, a connect with its Line run on - and left so until somebody formatted the file.
/// </summary>
/// <remarks>
/// <para><b>The incremental formatter's rule, not a new one</b> (<see cref="IncrementalFormatter"/>):
/// the file is formatted whole, with <see cref="StyleCheckingSettings.ToFormattingOptions"/> and the
/// checker's declaration-order lookup, and only where the desktop app would format it - the repository
/// has <see cref="StyleCheckingSettings.ApplyFormattingRules"/> on, and no class in the file is
/// excluded (<see cref="FormattingExclusion.Excludes"/>). A library from no repository has no rules to
/// apply, so its edits are written as composed. MLQT's premise is that a repository applying the rules
/// is already formatted, so formatting the whole file changes only what the edit wrote.</para>
///
/// <para>A text that does not parse is returned as it is: the caller's parse check refuses it, which
/// is the answer the edit should get, and rendering it would not be faithful.</para>
/// </remarks>
internal static class EditFormatting
{
    // Set for the length of an mlqt_edit_class batch, on that call's async flow only: its operations
    // each write the file and the next re-opens it, and a `replace` must match the text exactly - so
    // an operation's text reformatted before the next one runs made `replace "k=2"` after an
    // operation that wrote `k=2` fail and roll the whole batch back. The batch formats once, at its
    // end (FormatFileNow). An AsyncLocal rather than a flag on the singleton session, so a call
    // running beside it is formatted as usual.
    private static readonly AsyncLocal<bool> Deferred = new();

    /// <summary>
    /// Holds formatting off for the caller's async flow until the returned scope is disposed: what
    /// mlqt_edit_class wraps its operations in.
    /// </summary>
    public static IDisposable Defer()
    {
        var outer = Deferred.Value;
        Deferred.Value = true;
        return new Restore(outer);
    }

    private sealed class Restore(bool outer) : IDisposable
    {
        public void Dispose() => Deferred.Value = outer;
    }

    /// <summary>
    /// Formats the file holding <paramref name="classId"/> as it is on disk now, with its repository's
    /// rules, writing and reloading it if that changes anything: the one format a batch makes, after
    /// its last operation. Returns a ToolError only when the formatted file could not be written.
    /// </summary>
    public static async Task<object?> FormatFileNowAsync(
        ILibraryDataService libraries, IExternalResourceService resources, SessionState session, string classId)
    {
        if (ModelFilePersistence.ResolveFileOwner(libraries, classId) is not { } ctx || !File.Exists(ctx.FilePath))
            return null;

        var onDisk = await ModelicaFileEncoding.ReadAllTextOnlyAsync(ctx.FilePath);
        var text = ModelicaParserHelper.NormalizeLineEndings(onDisk);
        var formatted = Apply(libraries, session, ctx.FileOwner.Id, ctx.FileOwner.Id,
            ctx.FileOwner.ContainingFileId, text, ctx.FileOwner.ParentModelName);
        if (ReferenceEquals(formatted, text)
            || ModelicaFileEncoding.ForFileText(onDisk, formatted) == onDisk)
            return null;

        if (FileWritability.RequireWritable(ctx.FilePath, "format the edited class") is { } readOnly)
            return readOnly;
        await ModelicaFileEncoding.WriteAllTextAsync(ctx.FilePath, formatted);
        var affected = await libraries.ReloadFileAsync(ctx.FilePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, libraries, resources, session);
        return null;
    }

    /// <summary>
    /// <paramref name="fileContent"/> formatted with the rules governing <paramref name="governingClassId"/>,
    /// or unchanged where those rules do not format it.
    /// </summary>
    /// <param name="governingClassId">A class of the library the file belongs to: its owner, or for a
    /// file not yet in the graph, the class it is being created in.</param>
    /// <param name="rootClassId">The class heading the file, as the declaration-order lookup is keyed.</param>
    /// <param name="existingFileId">The file's node, for the exclusions its classes carry now; null for
    /// a file being created.</param>
    /// <param name="withinParent">The package the file's classes live in, if its text has no within clause.</param>
    /// <param name="incomingIds">The ids a moved class and its nested classes had before the move. The
    /// repository's excluded list names a class by id, and a move gives it a new one, so the list
    /// can only be asked under the old.</param>
    public static string Apply(
        ILibraryDataService libraries, SessionState session, string governingClassId, string rootClassId,
        string? existingFileId, string fileContent, string? withinParent, IEnumerable<string>? incomingIds = null)
    {
        if (Deferred.Value
            || session.Repositories is not { } repositories
            || RepositorySettings.ForClass(libraries, repositories, governingClassId) is not { ApplyFormattingRules: true } settings)
            return fileContent;

        var graph = libraries.CombinedGraph;
        if (existingFileId is not null && graph.GetModelsInFile(existingFileId).Any(m => FormattingExclusion.Excludes(m, settings)))
            return fileContent;
        if (settings.IsModelExcludedFromFormatting(rootClassId)
            || (incomingIds?.Any(settings.IsModelExcludedFromFormatting) ?? false)
            || OptsOutInText(fileContent, withinParent, settings))
            return fileContent;

        var formatting = settings.ToFormattingOptions();
        var rendered = ModelicaPackageSaver.RenderFileSource(
            fileContent, withinParent, formatting, out var errors, rootClassId: rootClassId,
            isSimpleType: formatting.DeclarationOrder ? StyleChecking.CreateSimpleTypeLookup(graph) : null);

        return errors.Count > 0 || string.IsNullOrWhiteSpace(rendered) ? fileContent : rendered;
    }

    /// <summary>
    /// Whether a class in the text being written is excluded from formatting, though the graph does not
    /// have it there yet: an edit can be the one that adds <c>__MLQT(format=false)</c>, and a move brings
    /// a class into a file whose classes the graph knows - including one on the repository's excluded
    /// list, which only its own id, now the new one, says. Asked of each class in the text through
    /// <see cref="FormattingExclusion.Excludes"/>, and only when the text could hold a directive or the
    /// list has anything on it, so an ordinary edit costs a string search.
    /// </summary>
    private static bool OptsOutInText(string fileContent, string? withinParent, StyleCheckingSettings settings)
    {
        if (!fileContent.Contains("__MLQT", StringComparison.Ordinal)
            && !fileContent.Contains("__Dymola_FMU", StringComparison.Ordinal)
            && settings.FormattingExcludedModels.Count == 0)
            return false;

        var (models, errors) = ModelicaParserHelper.ExtractModelsWithErrors(fileContent);
        if (errors.Count > 0)
            return false; // not rendered anyway

        // Ids as a load of this text would give them, which is how the incremental formatter re-reads
        // a file it has written.
        return models.Any(m => FormattingExclusion.Excludes(new ModelNode(
            GraphBuilder.GenerateModelId(m.ParentModelName ?? withinParent, m.Name), m.Name, m.SourceCode), settings));
    }
}
