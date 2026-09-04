using System.Text.Json;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>
/// SARIF 2.1.0 output for GitHub code scanning, Azure DevOps, and SARIF viewers.
///
/// <para>GitHub reads a documented subset of the format, and two things about that subset shape what
/// is written here. A rule descriptor needs <c>fullDescription</c> and <c>help</c> as well as
/// <c>shortDescription</c> — <c>help.markdown</c> is what renders in the alert body, so without it an
/// alert arrives naming a rule and saying nothing about it. And neither <c>baselineState</c> nor
/// <c>suppressions</c> is supported, so anything written here shows up as an open alert whatever it
/// is tagged with: see <see cref="CheckReport.SarifIncludeAccepted"/>.</para>
/// </summary>
internal sealed class SarifFindingFormatter : IFindingFormatter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Format(CheckReport report)
    {
        // Accepted debt is agreed, unactionable-in-this-PR history. GitHub cannot be told that, so by
        // default it is left out rather than shown as a wall of open alerts that buries the findings
        // the run is actually about.
        var reported = report.SarifIncludeAccepted
            ? report.Findings
            : report.Actionable.ToList();

        var rules = reported
            .Select(c => c.Finding.RuleId)
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id =>
            {
                RuleCatalog.BuiltIn.TryGetValue(id, out var def);
                var title = def?.Title ?? id;
                var description = def?.Description ?? id;
                var category = def?.Category ?? "Unknown";
                return new
                {
                    id,
                    name = title,
                    shortDescription = new { text = title },
                    fullDescription = new { text = description },
                    defaultConfiguration = new { level = Level(def?.DefaultSeverity ?? RuleSeverity.Warning) },
                    help = new
                    {
                        text = $"{description} ({category} rule {id}.) See {ToolInfo.InformationUri}",
                        markdown = Help(id, title, description, category)
                    },
                    helpUri = DocumentationUriFor(id),
                    properties = new { category, tags = new[] { category } }
                };
            })
            .ToList();

        var results = reported.Select(c =>
        {
            var f = c.Finding;
            return new
            {
                ruleId = f.RuleId,
                level = Level(f.Severity),
                message = new { text = f.Message },
                baselineState = BaselineState(c.Status),
                partialFingerprints = new Dictionary<string, string> { ["mlqt/v1"] = f.Fingerprint },
                locations = new object[]
                {
                    new
                    {
                        physicalLocation = new
                        {
                            artifactLocation = new { uri = RelativeUri(report, f) },
                            region = new { startLine = report.LineFor(f) }
                        }
                    }
                }
            };
        }).ToList();

        // A Dictionary is used for the top level so the "$schema" key (not a valid C# identifier) can be emitted.
        var sarif = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new object[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = ToolInfo.Name,
                            informationUri = ToolInfo.InformationUri,
                            version = ToolInfo.Version,
                            semanticVersion = ToolInfo.SemanticVersion,
                            rules
                        }
                    },
                    results
                }
            }
        };

        return JsonSerializer.Serialize(sarif, Options);
    }

    private static string Level(RuleSeverity severity) => severity switch
    {
        RuleSeverity.Error => "error",
        RuleSeverity.Info => "note",
        _ => "warning"
    };

    private static string BaselineState(FindingStatus status) =>
        status == FindingStatus.New ? "new" : "unchanged";

    /// <summary>
    /// What GitHub renders in the alert body. Everything MLQT knows about the rule, in the order a
    /// reader needs it: what is wrong, which family it belongs to, and what to do about it.
    ///
    /// <para>A diagnostic gets different words. It has no setting, so telling its reader to change
    /// the severity or switch it off describes a control that does not exist — and sending them to
    /// the settings reference sends them to a page that deliberately does not name it. Both were
    /// true of every parse-error alert until this branched: exactly the defect the rule alerts had
    /// fixed, on the three ids that fix excluded.</para>
    /// </summary>
    private static string Help(string id, string title, string description, string category) =>
        $"""
        ## {title}

        {description}

        **Rule:** `{id}` - **Category:** {category}

        {Advice(id)}
        """;

    /// <summary>The closing line of an alert body: what the reader can actually do.</summary>
    private static string Advice(string id) =>
        RuleIds.IsDiagnostic(id)
            ? $"""
              This is a diagnostic, not a style rule: it is always reported, it cannot be switched
              off or given a different severity, and it is never accepted into a baseline. It says
              the results you are reading are incomplete.
              See [the diagnostics reference]({DiagnosticDocumentationUri}).
              """
            : $"""
              Configure this rule's severity, or switch it off, in the repository's
              `.mlqt/settings.json`. See [the settings reference]({RuleDocumentationUri}).
              """;

    /// <summary>Where a rule id is documented. A dead link is worse than none, so each of these is a
    /// page that lists every id it covers rather than a per-rule anchor that may not exist — and
    /// <c>RuleDocumentationTests</c> holds both pages to the catalog.</summary>
    private static string DocumentationUriFor(string id) =>
        RuleIds.IsDiagnostic(id) ? DiagnosticDocumentationUri : RuleDocumentationUri;

    private const string RuleDocumentationUri =
        "https://github.com/mdempse1/MLQT/blob/main/Documentation/settings-reference.md";

    /// <summary>The diagnostics are on the CLI page, under "Diagnostics" — they are not settings, so
    /// the settings reference does not carry them.</summary>
    private const string DiagnosticDocumentationUri =
        "https://github.com/mdempse1/MLQT/blob/main/Documentation/cli.md#diagnostics";

    /// <summary>
    /// The file path a SARIF reader will resolve against its own root — the checked-out repository,
    /// for GitHub code scanning. Relative to <c>--sarif-base</c> when given, and to the library
    /// otherwise, which is the same thing when the library is the repository.
    /// </summary>
    private static string RelativeUri(CheckReport report, Finding f)
    {
        var abs = report.FileFor(f);
        if (abs is null)
            return f.ModelId;
        var relative = Path.GetRelativePath(report.SarifBasePath ?? report.LibraryPath, abs);
        return relative.Replace('\\', '/');
    }
}
