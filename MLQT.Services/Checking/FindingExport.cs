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
///
/// <para><b>Severity is the level on its own</b> — <c>"Warning"</c>, not the <c>"Style warning"</c>
/// the app's own issue list shows. The list carries the prefix so a style finding reads differently
/// from a parse diagnostic; this file has <c>Source</c> for that, and a severity column spelt one way
/// here and another in the CLI's report would disagree on every row of a diff. It comes from
/// <see cref="LogMessage.StyleSeverity"/>, the value stamped on the finding, so the two spellings
/// cannot drift apart.</para>
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
    /// is what the CLI does when it is pointed at a file rather than a package — including one found
    /// in a repository, whose source type says Git or SVN (<see cref="LoadedLibrary.RootDirectory"/>, B428).
    /// </remarks>
    public static Dictionary<string, string> LibraryRootsByModel(IEnumerable<LoadedLibrary> libraries)
    {
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var library in libraries)
        {
            var root = library.RootDirectory;

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
        var payload = new ExportDocument(
            ToolName,
            exportedAt.ToString("o"),
            projectName,
            findings.Count,
            findings
                .OrderBy(m => m.ModelName, StringComparer.Ordinal)
                .ThenBy(m => m.LineNumber)
                .ThenBy(m => m.RuleId ?? string.Empty, StringComparer.Ordinal)
                .Select(m => new ExportedFinding(
                    m.RuleId,
                    // The CLI writes the level on its own — "Warning", "Error", "Info" — so this does
                    // too, from the severity carried on the message rather than from its display
                    // string. The app's own list says "Style warning" to tell a style finding apart
                    // from a parse diagnostic at a glance; here `Source` already draws that line, and
                    // a second vocabulary in a file whose whole purpose is to be diffed against the
                    // CLI's would make the severity column disagree on every row. Spelled, not
                    // ToString(), which an obfuscated build turns into a number (B502).
                    m.StyleSeverity is { } style ? RuleSeverityText.Of(style) : m.Severity,
                    statusOf?.Invoke(m),
                    m.ModelName,
                    m.ElementPath,
                    ReportLocation.LineIn(locations.GetValueOrDefault(m.ModelName), m.LineNumber),
                    m.LineNumber,
                    m.Summary,
                    m.Fingerprint,
                    ReportLocation.RelativeFile(
                        locations.GetValueOrDefault(m.ModelName),
                        libraryRootByModel.GetValueOrDefault(m.ModelName)),
                    m.Source,
                    string.IsNullOrEmpty(m.Details) ? null : m.Details))
                .ToList());

        return JsonSerializer.Serialize(payload, Options);
    }

    // Compiled in, so an obfuscated build writes the field names a plain build and the CLI do (B502).
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = MlqtServicesJsonContext.Default,
    };

    /// <summary>The exported document. Its own fields are lower case, its findings' PascalCase - the
    /// names the file has always had, and the CLI's report shares the findings' (B502: written down
    /// rather than taken from the members).</summary>
    internal sealed record ExportDocument(
        [property: JsonPropertyName("tool")] string Tool,
        [property: JsonPropertyName("exported")] string Exported,
        [property: JsonPropertyName("project")] string? Project,
        [property: JsonPropertyName("findingCount")] int FindingCount,
        [property: JsonPropertyName("findings")] IReadOnlyList<ExportedFinding> Findings);

    /// <summary>One finding in the export.</summary>
    internal sealed record ExportedFinding(
        [property: JsonPropertyName("RuleId")] string? RuleId,
        [property: JsonPropertyName("Severity")] string? Severity,
        [property: JsonPropertyName("Status")] string? Status,
        [property: JsonPropertyName("Model")] string Model,
        [property: JsonPropertyName("Element")] string? Element,
        [property: JsonPropertyName("Line")] int Line,
        [property: JsonPropertyName("ModelLine")] int ModelLine,
        [property: JsonPropertyName("Message")] string Message,
        [property: JsonPropertyName("Fingerprint")] string? Fingerprint,
        [property: JsonPropertyName("File")] string? File,
        [property: JsonPropertyName("Source")] string Source,
        [property: JsonPropertyName("Details")] string? Details);

    /// <summary>The file name to write, which carries the moment so two exports never collide.</summary>
    public static string FileNameFor(DateTime exportedAt) =>
        $"mlqt-findings-{exportedAt:yyyyMMdd-HHmmss}.json";
}
