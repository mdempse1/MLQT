using System.Xml.Linq;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>
/// JUnit XML: each actionable finding (new, or touched debt) is emitted as a failing test case, so
/// any CI test-report UI (TeamCity, Jenkins, GitLab, Azure) renders findings natively with no custom
/// integration. Accepted debt is omitted, so a green build means "no new debt."
/// </summary>
internal sealed class JUnitFindingFormatter : IFindingFormatter
{
    public string Format(CheckReport report)
    {
        var actionable = report.Findings
            .Where(c => c.Status != FindingStatus.AcceptedDebt)
            .ToList();
        var count = actionable.Count;

        var testcases = actionable.Select(c =>
        {
            var f = c.Finding;
            var file = report.RelativeFileFor(f);
            var line = report.LineFor(f);
            var name = f.ElementPath is null
                ? $"{f.RuleId} (line {line})"
                : $"{f.RuleId}:{f.ElementPath} (line {line})";

            return new XElement("testcase",
                new XAttribute("classname", f.ModelId), // the model is the "class" — groups findings by model in the CI test UI
                new XAttribute("name", name),
                new XElement("failure",
                    new XAttribute("message", f.Message),
                    new XAttribute("type", c.Status == FindingStatus.TouchedDebt ? $"{f.RuleId} (touched debt)" : f.RuleId),
                    new XText($"{f.ModelId}{(file is null ? "" : $" ({file})")} line {line}: {f.Message}")));
        });

        var suite = new XElement("testsuite",
            new XAttribute("name", report.LibraryPath),
            new XAttribute("tests", count),
            new XAttribute("failures", count),
            testcases);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("testsuites",
                new XAttribute("name", "mlqt"),
                new XAttribute("tests", count),
                new XAttribute("failures", count),
                suite));

        return doc.Declaration + Environment.NewLine + doc;
    }
}
