using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Editing tools that change the source of loaded classes. Currently just update_class_source, which
/// replaces a single class's body in place (the class name must stay the same). It writes the affected
/// .mo file, reloads it, and (when analysis has run) refreshes the dependency graph. Renaming/moving a
/// class — which must also rewrite references — is deliberately not offered yet; it needs precise
/// reference tracking in the parser/graph layer rather than a textual best-effort.
/// </summary>
[McpServerToolType]
public sealed class EditTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public EditTools(ILibraryDataService libraries, IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
    }

    [McpServerTool(Name = "update_class_source")]
    [Description("Replace the Modelica source of a single loaded class with new source, then write the " +
                "file to disk and refresh the graph (so check_class / spell_check / get_class_source / the " +
                "dependency tools see the change). new_source must be ONE complete, syntactically valid " +
                "class definition (e.g. 'model X ... end X;'); it is written verbatim — NOT reformatted, so " +
                "run format_class afterwards if you want. The class name must stay the same: renaming or " +
                "moving a class is not supported yet (it would need to rewrite references too). Set " +
                "preview=true to get the resulting file text without writing.")]
    public async Task<object> UpdateClassSource(
        [Description("Fully-qualified class id to replace, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId,
        [Description("The new Modelica source: exactly one complete class definition, with the same class name.")]
        string newSource,
        [Description("Return the resulting file text without writing to disk or updating the graph. Default false.")]
        bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(newSource))
            return new ToolError("new_source must be a non-empty, complete Modelica class definition.");

        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse; its source range is unknown and cannot be updated.");

        var (models, errors) = ModelicaParserHelper.ExtractModelsWithErrors(newSource);
        if (errors.Count > 0)
            return new ToolError(
                $"new_source has {errors.Count} syntax error(s): {DescribeErrors(errors)}. Provide one " +
                "complete, valid class definition (e.g. 'model X ... end X;').");

        var topLevel = models.Where(m => !m.IsNested).ToList();
        if (topLevel.Count != 1)
            return new ToolError(
                $"new_source must define exactly ONE top-level class; found {topLevel.Count}. Update one class at a time.");
        if (!string.Equals(topLevel[0].Name, node.Name, StringComparison.Ordinal))
            return new ToolError(
                $"new_source renames the class from '{node.Name}' to '{topLevel[0].Name}'. update_class_source " +
                "only replaces a class's body in place — renaming (which must also update references elsewhere) " +
                "is not supported yet. Keep the class name the same.");

        var ctx = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (ctx is null)
            return new ToolError($"Could not locate the source file for '{classId}'.");

        var owner = ctx.FileOwner;
        var ownerCode = owner.Definition.ModelicaCode ?? string.Empty;

        string newOwnerCode;
        if (node.Id == owner.Id)
        {
            newOwnerCode = newSource;
        }
        else
        {
            var oldClassCode = node.Definition.ModelicaCode ?? string.Empty;
            if (CountOccurrences(ownerCode, oldClassCode) != 1)
                return new ToolError(
                    "Could not uniquely locate the class within its file (its cached source may be stale). " +
                    "Reload the library and retry.");
            newOwnerCode = ReplaceFirst(ownerCode, oldClassCode, newSource);
        }

        var fileContent = PrependWithinClause(newOwnerCode, owner.ParentModelName);

        if (preview)
            return new UpdateClassSourceResult(classId, ctx.FilePath, PreviewOnly: true, Changed: false, 0, fileContent);

        await File.WriteAllTextAsync(ctx.FilePath, fileContent);
        var affected = await _libraries.ReloadFileAsync(ctx.FilePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        return new UpdateClassSourceResult(classId, ctx.FilePath, PreviewOnly: false, Changed: true, affected.Count, null);
    }

    private static string PrependWithinClause(string ownerCode, string? parentModelName)
    {
        if (ownerCode.StartsWith("within", StringComparison.Ordinal))
            return ownerCode;
        return string.IsNullOrEmpty(parentModelName)
            ? "within;\n" + ownerCode
            : $"within {parentModelName};\n{ownerCode}";
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
            return 0;
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string source, string oldValue, string newValue)
    {
        var idx = source.IndexOf(oldValue, StringComparison.Ordinal);
        return idx < 0 ? source : source[..idx] + newValue + source[(idx + oldValue.Length)..];
    }

    private static string DescribeErrors(IReadOnlyList<ParserError> errors)
    {
        var shown = errors.Take(5).Select(e => $"line {e.Line}:{e.CharPosition} {e.Message}");
        return string.Join("; ", shown) + (errors.Count > 5 ? $" (+{errors.Count - 5} more)" : "");
    }
}
