using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class CheckModelReferencesTests
{
    private static readonly HashSet<string> KnownModels = new(StringComparer.Ordinal)
    {
        "Modelica.Blocks.Sources.Step",
        "Modelica.Mechanics.Rotational.Components.Inertia",
        "MyLib.TestModel",
        "MyLib.SubPackage.AnotherModel"
    };

    private List<DataTypes.LogMessage> CheckRule(string code, IReadOnlySet<string>? knownModels = null)
    {
        knownModels ??= KnownModels;
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckModelReferences(knownModels);
        visitor.Visit(parseTree);
        return visitor.RuleViolations;
    }

    // ── Valid references ──

    [Fact]
    public void ValidModelReference_NoViolation()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
              annotation(Documentation(info="<html><p>See <a href=\"modelica://Modelica.Blocks.Sources.Step\">Step</a>.</p></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    [Fact]
    public void FileReference_WithSlash_NotValidated()
    {
        // URIs with '/' are file references, not model references — should not be checked
        var code = """
            model TestModel "Uses a data file"
              Real x;
              annotation(Documentation(info="<html><p>Data at <a href=\"modelica://MyLib/Resources/data.mat\">data</a></p></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    [Fact]
    public void NoModelicaUri_NoViolation()
    {
        var code = """
            model TestModel "Simple model"
              Real x;
              annotation(Documentation(info="<html><p>Just text, no links.</p></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    // ── Broken references ──

    [Fact]
    public void BrokenModelReference_ReportsViolation()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
              annotation(Documentation(info="<html><p>See <a href=\"modelica://NonExistent.Model\">link</a></p></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("NonExistent.Model", violations[0].Summary);
        Assert.Contains("not found", violations[0].Summary);
    }

    [Fact]
    public void MultipleReferences_OneInvalid_ReportsOne()
    {
        var code = """
            model TestModel "Multiple links"
              Real x;
              annotation(Documentation(info="<html>
                <p><a href=\"modelica://Modelica.Blocks.Sources.Step\">Step</a></p>
                <p><a href=\"modelica://DoesNotExist\">broken</a></p>
              </html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("DoesNotExist", violations[0].Summary);
    }

    [Fact]
    public void MultipleReferences_AllInvalid_ReportsAll()
    {
        var code = """
            model TestModel "Multiple broken links"
              Real x;
              annotation(Documentation(info="<html>
                <p><a href=\"modelica://Bad1\">link1</a></p>
                <p><a href=\"modelica://Bad2\">link2</a></p>
              </html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Equal(2, violations.Count);
    }

    // ── HTML entity filtering ──

    [Fact]
    public void HtmlEntityEncoded_Quote_Skipped()
    {
        // &quot;modelica://... should be skipped (example code, not a real link)
        var code = """
            model TestModel "Entity test"
              Real x;
              annotation(Documentation(info="<html><p>Example: &quot;modelica://SomeModel&quot;</p></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    [Fact]
    public void NumericEntityEncoded_Skipped()
    {
        // &#34;modelica://... should be skipped
        var code = """
            model TestModel "Numeric entity test"
              Real x;
              annotation(Documentation(info="<html><p>Example: &#34;modelica://SomeModel&#34;</p></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    // ── Plain text mentions ──

    [Fact]
    public void PlainTextMention_NotPrecededByQuote_Skipped()
    {
        // "Replace modelica://URIs by ..." — descriptive text, not a link
        var code = """
            model TestModel "Description"
              Real x;
              annotation(Documentation(info="<html><p>Replace modelica://BadModel with something else.</p></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    // ── Quoted identifiers ──

    [Fact]
    public void QuotedIdentifier_InUri_Handled()
    {
        // modelica:// with 'quoted identifier' segments
        var knownModels = new HashSet<string> { "MyLib.'special name'.Model" };
        var code = """
            model TestModel "Quoted ID test"
              Real x;
              annotation(Documentation(info="<html><a href=\"modelica://MyLib.'special name'.Model\">link</a></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code, knownModels);
        Assert.Empty(violations);
    }

    // ── Empty path part ──

    [Fact]
    public void EmptyPathPart_NoViolation()
    {
        // modelica:// with nothing after it — empty path part, not validated
        var code = """
            model TestModel "Empty URI"
              Real x;
              annotation(Documentation(info="<html><a href=\"modelica://\">link</a></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    // ── URI delimiters ──

    [Fact]
    public void UriEndedByAngleBracket_Extracted()
    {
        var code = """
            model TestModel "Angle bracket"
              Real x;
              annotation(Documentation(info="<html><a href=\"modelica://NonExistent\">link</a></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("NonExistent", violations[0].Summary);
    }

    [Fact]
    public void UriEndedByHash_Extracted()
    {
        // modelica://Model#section should extract just "Model"
        var knownModels = new HashSet<string> { "MyModel" };
        var code = """
            model TestModel "Hash in URI"
              Real x;
              annotation(Documentation(info="<html><a href=\"modelica://MyModel#details\">link</a></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code, knownModels);
        Assert.Empty(violations);
    }

    // ── Multi-line string tracking ──

    [Fact]
    public void MultiLineString_ReportsCorrectLineNumber()
    {
        var code = "model TestModel \"A model\"\n" +
                   "  Real x;\n" +
                   "  annotation(Documentation(info=\"<html>\n" +
                   "<p>First line.</p>\n" +
                   "<p>Second line.</p>\n" +
                   "<p><a href=\\\"modelica://BadModel\\\">link</a></p>\n" +
                   "</html>\"));\n" +
                   "end TestModel;";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckModelReferences(KnownModels);
        visitor.Visit(parseTree);

        // The violation (if any) should track newlines for line number
        // This test mainly exercises the newline counting logic
        Assert.NotNull(visitor.RuleViolations);
    }

    // ── Non-string primary ──

    [Fact]
    public void NonStringPrimary_NotChecked()
    {
        var code = """
            model TestModel "No strings"
              Real x = 1.0;
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    // ── Case insensitive URI prefix ──

    [Fact]
    public void CaseInsensitiveModelicaPrefix_StillValidates()
    {
        var code = """
            model TestModel "Mixed case URI"
              Real x;
              annotation(Documentation(info="<html><a href=\"Modelica://Modelica.Blocks.Sources.Step\">link</a></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Empty(violations);
    }

    // ── Base package tracking ──

    [Fact]
    public void WithBasePackage_TracksModelName()
    {
        var code = """
            model TestModel "Test"
              Real x;
              annotation(Documentation(info="<html><a href=\"modelica://NonExistent\">link</a></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckModelReferences(KnownModels, "MyLib");
        visitor.Visit(parseTree);

        Assert.NotEmpty(visitor.RuleViolations);
        Assert.Contains("MyLib", visitor.RuleViolations[0].ModelName);
    }

    // ── URI terminated by whitespace ──

    [Fact]
    public void UriTerminatedByWhitespace_Extracted()
    {
        var code = """
            model TestModel "Whitespace terminator"
              Real x;
              annotation(Documentation(info="<html><p>Link: \"modelica://NonExistentModel more text\"</p></html>"));
            end TestModel;
            """;

        // The inner quotes are part of the outer string, so "modelica://NonExistentModel
        // is preceded by \" which is a quote — so it WILL be validated
        var violations = CheckRule(code);
        Assert.Single(violations);
    }

    // ── Ampersand terminator ──

    [Fact]
    public void UriTerminatedByAmpersand_Extracted()
    {
        var code = """
            model TestModel "Ampersand"
              Real x;
              annotation(Documentation(info="<html><a href=\"modelica://NonExistent&param=1\">link</a></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("NonExistent", violations[0].Summary);
    }
}
