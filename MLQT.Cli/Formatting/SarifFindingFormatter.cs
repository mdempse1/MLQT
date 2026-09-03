using System.Text.Json;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>SARIF 2.1.0 output for GitHub code scanning, Azure DevOps, and SARIF viewers.</summary>
internal sealed class SarifFindingFormatter : IFindingFormatter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Format(CheckReport report)
    {
        var rules = report.Findings
            .Select(c => c.Finding.RuleId)
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id =>
            {
                RuleCatalog.BuiltIn.TryGetValue(id, out var def);
                return new
                {
                    id,
                    name = def?.Title ?? id,
                    shortDescription = new { text = def?.Description ?? id },
                    defaultConfiguration = new { level = Level(def?.DefaultSeverity ?? RuleSeverity.Warning) },
                    properties = new { category = def?.Category ?? "Unknown" }
                };
            })
            .ToList();

        var results = report.Findings.Select(c =>
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
                    tool = new { driver = new { name = "mlqt", rules } },
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
    private static string RelativeUri(CheckReport report, Finding f)
    {
        var abs = report.FileFor(f);
        if (abs is null)
            return f.ModelId;
        var relative = Path.GetRelativePath(report.SarifBasePath ?? report.LibraryPath, abs);
        return relative.Replace('\\', '/');
    }
}
