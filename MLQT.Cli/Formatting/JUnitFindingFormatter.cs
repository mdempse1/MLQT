using System.Xml.Linq;

namespace MLQT.Cli;

/// <summary>
/// JUnit XML: each finding is emitted as a failing test case, so any CI test-report UI
/// (TeamCity, Jenkins, GitLab, Azure) renders the findings natively with no custom integration.
/// </summary>
internal sealed class JUnitFindingFormatter : IFindingFormatter
{
    public string Format(CheckReport report)
    {
        var count = report.Findings.Count;

        var testcases = report.Findings.Select(f =>
        {
            var classname = report.FileFor(f) ?? f.ModelId;
            var name = f.ElementPath is null
                ? $"{f.RuleId} (line {f.LineNumber})"
                : $"{f.RuleId}:{f.ElementPath} (line {f.LineNumber})";

            return new XElement("testcase",
                new XAttribute("classname", classname),
                new XAttribute("name", name),
                new XElement("failure",
                    new XAttribute("message", f.Message),
                    new XAttribute("type", f.RuleId),
                    new XText($"{f.ModelId} line {f.LineNumber}: {f.Message}")));
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
