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
            : report.Findings.Where(c => c.Status != FindingStatus.AcceptedDebt).ToList();

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
                    helpUri = RuleDocumentationUri,
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
    /// The file path a SARIF reader will resolve against its own root — the checked-out repository,
    /// for GitHub code scanning. Relative to <c>--sarif-base</c> when given, and to the library
    /// otherwise, which is the same thing when the library is the repository.
    /// </summary>
    /// <summary>
    /// What GitHub renders in the alert body. Everything MLQT knows about the rule, in the order a
    /// reader needs it: what is wrong, which family it belongs to, and where the rule is documented.
    /// </summary>
    private static string Help(string id, string title, string description, string category) =>
        $"""
        ## {title}

        {description}

        **Rule:** `{id}` - **Category:** {category}

        Configure this rule's severity, or switch it off, in the repository's `.mlqt/settings.json`.
        See [the settings reference]({RuleDocumentationUri}).
        """;

    /// <summary>Where the rules are documented. A dead link is worse than none, so this is the one
    /// page that lists every rule id rather than a per-rule anchor that may not exist.</summary>
    private const string RuleDocumentationUri =
        "https://github.com/mdempse1/MLQT/blob/main/Documentation/settings-reference.md";

    private static string RelativeUri(CheckReport report, Finding f)
    {
        var abs = report.FileFor(f);
        if (abs is null)
            return f.ModelId;
        var relative = Path.GetRelativePath(report.SarifBasePath ?? report.LibraryPath, abs);
        return relative.Replace('\\', '/');
    }
}
