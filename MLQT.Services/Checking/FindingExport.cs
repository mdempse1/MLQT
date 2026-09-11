using System.Text.Json;
using System.Text.Json.Serialization;
using ModelicaParser.DataTypes;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// The desktop app's finding export, in the shape <c>mlqt check --format json</c> writes.
/// </summary>
/// <remarks>
/// <para>Backlog B119, the remainder of B20 for <c>CodeReview</c>: this was a hundred lines inline in
/// the page, reachable only by rendering it and clicking Export, and so tested by nobody.</para>
///
/// <para><b>Field names and meanings match the CLI's findings array</b>, so the two exports can be
/// diffed without translating between them. The names matched on their own for a while and the
/// meanings did not: B1 gave every report the line in the *file* and left this export on the
/// class-relative line the code viewer wants, so <c>build/Compare-Findings.ps1</c> — which pairs the
/// two up on model, rule and line — reported nearly every finding as exclusive to both sides. Both
/// numbers are written now, and the line and path come from <see cref="ReportLocation"/>, which is
/// the same code the CLI's report uses rather than a second copy of the rule.</para>
/// </remarks>
public static class FindingExport
{
    /// <summary>The tool name written into the payload, distinguishing it from the CLI's output.</summary>
    public const string ToolName = "mlqt-gui";

    /// <summary>
    /// Which library each class belongs to on disk, so a finding's path can be written relative to it.
    /// </summary>
    /// <remarks>
    /// A library loaded from a single <c>.mo</c> file is rooted at the directory containing it, which
    /// is what the CLI does when it is pointed at a file rather than a package.
    /// </remarks>
    public static Dictionary<string, string> LibraryRootsByModel(IEnumerable<LoadedLibrary> libraries)
    {
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var library in libraries)
        {
            var root = library.SourceType == LibrarySourceType.File
                ? Path.GetDirectoryName(library.SourcePath) ?? library.SourcePath
                : library.SourcePath;

            if (string.IsNullOrEmpty(root))
                continue;

            foreach (var id in library.ModelIds)
                roots[id] = root;
        }

        return roots;
    }

    /// <summary>The exported document, as JSON.</summary>
    /// <param name="findings">What to export. Ordered here rather than by the caller, so two exports
    /// of the same findings are the same file.</param>
    /// <param name="locations">Where each class starts, for turning a class-relative line into a file line.</param>
    /// <param name="libraryRootByModel">From <see cref="LibraryRootsByModel"/>.</param>
    /// <param name="projectName">The active project, or null.</param>
    /// <param name="exportedAt">Stamped into the payload; a parameter so a test can pin it.</param>
    /// <param name="statusOf">A finding's baseline classification, or null when there is no baseline.</param>
    public static string ToJson(
        IReadOnlyList<LogMessage> findings,
        IReadOnlyDictionary<string, ClassLocation> locations,
        IReadOnlyDictionary<string, string> libraryRootByModel,
        string? projectName,
        DateTime exportedAt,
        Func<LogMessage, string?>? statusOf = null)
    {
        var payload = new
        {
            tool = ToolName,
            exported = exportedAt.ToString("o"),
            project = projectName,
            findingCount = findings.Count,
            findings = findings
                .OrderBy(m => m.ModelName, StringComparer.Ordinal)
                .ThenBy(m => m.LineNumber)
                .ThenBy(m => m.RuleId ?? string.Empty, StringComparer.Ordinal)
                .Select(m => new
                {
                    RuleId = m.RuleId,
                    Severity = m.Severity,
                    Status = statusOf?.Invoke(m),
                    Model = m.ModelName,
                    Element = m.ElementPath,
                    Line = ReportLocation.LineIn(locations.GetValueOrDefault(m.ModelName), m.LineNumber),
                    ModelLine = m.LineNumber,
                    Message = m.Summary,
                    Fingerprint = m.Fingerprint,
                    File = ReportLocation.RelativeFile(
                        locations.GetValueOrDefault(m.ModelName),
                        libraryRootByModel.GetValueOrDefault(m.ModelName)),
                    Source = m.Source,
                    Details = string.IsNullOrEmpty(m.Details) ? null : m.Details,
                })
                .ToList(),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
    }

    /// <summary>The file name to write, which carries the moment so two exports never collide.</summary>
    public static string FileNameFor(DateTime exportedAt) =>
        $"mlqt-findings-{exportedAt:yyyyMMdd-HHmmss}.json";
}
