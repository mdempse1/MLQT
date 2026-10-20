using System.Text.RegularExpressions;
using ModelicaGraph.DataTypes;
using MLQT.McpServer.Dtos;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Search the source of every loaded class for text, the way an agent otherwise greps <c>.mo</c> files:
/// in the 2026-10-18 sessions about 240 reads went round MLQT through grep, sed and cat, looking for
/// where a name is used, a <c>Placement</c> or a <c>connect</c> (B503).
/// </summary>
/// <remarks>
/// <para><b>Each line is reported once, for the innermost class that holds it.</b> A class's stored
/// source includes the classes nested in it that are written inline, and each of those is a class of
/// its own; searching both would report the line twice. So each nested class's text is masked out of
/// its parent's before the parent is searched.</para>
/// <para><b>A file line is given only when it can be trusted</b>: when the stored source is still the
/// file's slice (<see cref="ModelNode.SourceMatchesFile"/>), through <see cref="ModelNode.TrimElision"/>
/// for a package whose standalone children were cut out. Otherwise the line within the class stands
/// alone, as a finding's does.</para>
/// </remarks>
internal static class CodeSearch
{
    public const int MaxLimit = 500;
    private const int MaxLineLength = 200;

    public static object Run(
        ILibraryDataService libraries, string query, bool regex, bool ignoreCase, string? libraryId,
        int limit, int offset)
    {
        if (string.IsNullOrEmpty(query))
            return new ToolError("query must be the text to look for in the code.");

        Func<string, bool> matches;
        if (regex)
        {
            Regex pattern;
            try
            {
                pattern = new Regex(query, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : 0),
                    TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex)
            {
                return new ToolError($"query is not a valid regular expression: {ex.Message}");
            }
            matches = line => pattern.IsMatch(line);
        }
        else
        {
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            matches = line => line.Contains(query, comparison);
        }

        string? onlyLibrary = null;
        if (libraryId is not null)
        {
            var (library, error) = EntityResolver.ResolveLibrary(libraries, libraryId);
            if (error is not null)
                return error;
            onlyLibrary = library!.Id;
        }

        var models = libraries.GetAllModels()
            .Where(m => !m.IsExternalStub && !m.IsParseFailurePlaceholder)
            .Where(m => onlyLibrary is null || libraries.GetOwningLibrary(m.Id)?.Id == onlyLibrary)
            .ToList();
        var nestedByParent = models
            .Where(m => m.IsNested && m.ParentModelName is not null)
            .GroupBy(m => m.ParentModelName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var items = new List<CodeSearchItem>();
        var total = 0;
        try
        {
            foreach (var model in models.OrderBy(m => m.Id, StringComparer.Ordinal))
            {
                var code = model.Definition.ModelicaCode;
                if (string.IsNullOrEmpty(code))
                    continue;
                var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                var masked = NestedLines(code, lines.Length, nestedByParent.GetValueOrDefault(model.Id));
                string? filePath = null;
                for (var i = 0; i < lines.Length; i++)
                {
                    if (masked.Contains(i) || !matches(lines[i]))
                        continue;
                    total++;
                    if (total <= offset || items.Count >= limit)
                        continue;
                    filePath ??= FilePathOf(libraries, model);
                    items.Add(new CodeSearchItem(model.Id, i + 1, filePath, FileLine(model, i + 1), Shorten(lines[i])));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new ToolError("The regular expression took too long on a line; simplify it.");
        }

        return new CodeSearchResult(query, total, offset, items.Count, total > offset + items.Count, items);
    }

    // The 0-based lines of 'code' that belong to a class nested in it with a node of its own.
    private static HashSet<int> NestedLines(string code, int lineCount, List<ModelNode>? nested)
    {
        var masked = new HashSet<int>();
        if (nested is null)
            return masked;
        var text = code.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var child in nested)
        {
            var childCode = child.Definition.ModelicaCode?.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (string.IsNullOrEmpty(childCode))
                continue;
            var at = text.IndexOf(childCode, StringComparison.Ordinal);
            if (at < 0)
                continue;
            var first = text.AsSpan(0, at).Count('\n');
            var last = first + childCode.AsSpan().Count('\n');
            for (var line = first; line <= last && line < lineCount; line++)
                masked.Add(line);
        }
        return masked;
    }

    private static string? FilePathOf(ILibraryDataService libraries, ModelNode model) =>
        model.ContainingFileId is { } fileId ? libraries.CombinedGraph.GetNode<FileNode>(fileId)?.FilePath : null;

    private static int? FileLine(ModelNode model, int classLine)
    {
        if (!model.SourceMatchesFile || model.StartLine <= 0)
            return null;
        var sourceLine = model.TrimElision?.ToSourceLine(classLine) ?? classLine;
        return model.StartLine + sourceLine - 1;
    }

    private static string Shorten(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= MaxLineLength ? trimmed : trimmed[..MaxLineLength] + "…";
    }
}
