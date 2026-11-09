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
    public static string Apply(
        ILibraryDataService libraries, SessionState session, string governingClassId, string rootClassId,
        string? existingFileId, string fileContent, string? withinParent)
    {
        if (session.Repositories is not { } repositories
            || RepositorySettings.ForClass(libraries, repositories, governingClassId) is not { ApplyFormattingRules: true } settings)
            return fileContent;

        var graph = libraries.CombinedGraph;
        if (existingFileId is not null && graph.GetModelsInFile(existingFileId).Any(m => FormattingExclusion.Excludes(m, settings)))
            return fileContent;
        if (settings.IsModelExcludedFromFormatting(rootClassId) || OptsOutInText(fileContent, withinParent))
            return fileContent;

        var formatting = settings.ToFormattingOptions();
        var rendered = ModelicaPackageSaver.RenderFileSource(
            fileContent, withinParent, formatting, out var errors, rootClassId: rootClassId,
            isSimpleType: formatting.DeclarationOrder ? StyleChecking.CreateSimpleTypeLookup(graph) : null);

        return errors.Count > 0 || string.IsNullOrWhiteSpace(rendered) ? fileContent : rendered;
    }

    /// <summary>
    /// Whether the text being written opts a class out itself - an edit can be the one that adds
    /// <c>__MLQT(format=false)</c>, and the graph has not seen it yet. Asked of each class in the text
    /// through <see cref="FormattingExclusion.OptsOutInSource"/>, and only when the text could hold a
    /// directive at all, so an ordinary edit costs a string search.
    /// </summary>
    private static bool OptsOutInText(string fileContent, string? withinParent)
    {
        if (!fileContent.Contains("__MLQT", StringComparison.Ordinal)
            && !fileContent.Contains("__Dymola_FMU", StringComparison.Ordinal))
            return false;

        var (models, errors) = ModelicaParserHelper.ExtractModelsWithErrors(fileContent);
        if (errors.Count > 0)
            return false; // not rendered anyway

        return models.Any(m => FormattingExclusion.OptsOutInSource(new ModelNode(
            GraphBuilder.GenerateModelId(m.ParentModelName ?? withinParent, m.Name), m.Name, m.SourceCode)));
    }
}
