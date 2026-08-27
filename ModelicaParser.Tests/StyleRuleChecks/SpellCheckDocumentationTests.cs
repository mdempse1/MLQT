using Xunit;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class SpellCheckDocumentationTests
{
    private static SpellChecker CreateSpellChecker() => SpellChecker.Create();

    [Fact]
    public void CorrectDocumentation_NoFindings()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>This is a simple model for testing.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void MisspelledInDocumentation_ReportsFinding()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>This has a misspeling in the documentation.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Contains(visitor.RuleFindings, v => v.Summary.Contains("misspeling"));
        Assert.Contains(visitor.RuleFindings, v => v.Summary.Contains("documentation info"));
    }

    [Fact]
    public void MisspelledInRevisions_ReportsFinding()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(
                info="<html><p>Correct documentation.</p></html>",
                revisions="<html><p>Fixed a misspeling in the code.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Contains(visitor.RuleFindings, v =>
            v.Summary.Contains("misspeling") && v.Summary.Contains("documentation revisions"));
    }

    [Fact]
    public void HtmlTagsNotSpellChecked()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><body><p>Simple text here.</p></body></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        // HTML tag names like 'html', 'body', 'p' should not be flagged
        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void CodeBlocksSkipped()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Good text.</p><code>xyzzy foobar</code><p>More good text.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        // 'xyzzy' and 'foobar' are inside <code> blocks and should be stripped
        var codeFindings = visitor.RuleFindings
            .Where(v => v.Summary.Contains("xyzzy") || v.Summary.Contains("foobar"))
            .ToList();
        Assert.Empty(codeFindings);
    }

    [Fact]
    public void HtmlEntitiesDecoded()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Temperature &amp; pressure are correct.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        // &amp; should be decoded to & and not cause issues
        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void NoDocumentation_NoFindings()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void EmptyDocumentationString_NoFindings()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info=""));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void ModelicaTermsInDocumentation_NoFalsePositives()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>This model uses Modelica linearization and Jacobian analysis.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void ModelNameInDocumentation_NoFinding()
    {
        var knownModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Step", "Integrator"
        };

        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>This model wraps a Step source.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker(), knownModelNames);
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void ComponentNameInDocumentation_NoFinding()
    {
        var code = """
            model TestModel "A simple model"
              Real rflx "Reflux value";
              Real other "Another value";
            equation
              rflx = 1.0;
              other = rflx;
              annotation(Documentation(info="<html><p>The rflx component holds the result.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        var rflxFindings = visitor.RuleFindings
            .Where(v => v.Summary.Contains("'rflx'"))
            .ToList();
        Assert.Empty(rflxFindings);
    }

    [Fact]
    public void NonDocumentationAnnotation_NotChecked()
    {
        // Annotations that are not Documentation should not be spell-checked
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}})));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void PreBlocksSkipped()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Good text.</p><pre>xyzzy nonsenseword</pre></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        var preFindings = visitor.RuleFindings
            .Where(v => v.Summary.Contains("xyzzy") || v.Summary.Contains("nonsenseword"))
            .ToList();
        Assert.Empty(preFindings);
    }

    [Fact]
    public void MultiLineDocumentation_ReportsCorrectLineNumber()
    {
        // The documentation string starts on line 6 and the misspelled word is several lines in
        var code = "model TestModel \"A simple model\"\n" +
                   "  Real x;\n" +
                   "equation\n" +
                   "  x = 1.0;\n" +
                   "  annotation(Documentation(info=\"<html>\n" +
                   "<p>First paragraph is correct.</p>\n" +
                   "<p>Second paragraph is also correct.</p>\n" +
                   "<p>Third paragraph has a misspeling.</p>\n" +
                   "</html>\"));\n" +
                   "end TestModel;";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        var finding = visitor.RuleFindings.FirstOrDefault(v => v.Summary.Contains("misspeling"));
        Assert.NotNull(finding);
        // The STRING token starts on line 5 (annotation line), and the misspelled word
        // is 3 newlines into the string content, so it should be on line 8
        Assert.Equal(8, finding.LineNumber);
    }

    [Fact]
    public void MultiLineDocumentation_WithPreBlock_ReportsCorrectLineNumber()
    {
        // Pre block spans lines 7-9, misspelled word is on line 11
        var code = "model TestModel \"A simple model\"\n" +       // line 1
                   "  Real x;\n" +                                // line 2
                   "equation\n" +                                 // line 3
                   "  x = 1.0;\n" +                               // line 4
                   "  annotation(Documentation(info=\"<html>\n" + // line 5 (STRING starts here)
                   "<p>Correct text.</p>\n" +                     // line 6
                   "<pre>code line 1\n" +                         // line 7
                   "code line 2\n" +                              // line 8
                   "code line 3</pre>\n" +                        // line 9
                   "<p>More correct text.</p>\n" +                // line 10
                   "<p>Has a misspeling here.</p>\n" +            // line 11
                   "</html>\"));\n" +                             // line 12
                   "end TestModel;";                              // line 13

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        var finding = visitor.RuleFindings.FirstOrDefault(v => v.Summary.Contains("misspeling"));
        Assert.NotNull(finding);
        // The misspelled word is on line 11
        Assert.Equal(11, finding.LineNumber);
    }

    [Fact]
    public void DocumentationWithHtmlEntities_NoFalsePositives()
    {
        // HTML entities like &Delta; &zeta; &rho; &pi; should not be flagged
        var code = "model TestModel \"A simple model\"\n" +
                   "  Real x;\n" +
                   "equation\n" +
                   "  x = 1.0;\n" +
                   "  annotation(Documentation(info=\"<html>\n" +
                   "<p>The pressure drop is defined as:</p>\n" +
                   "<p>&Delta;p = 0.5*&zeta;*&rho;*v*|v|</p>\n" +
                   "</html>\"));\n" +
                   "end TestModel;";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        // No findings should be reported for decoded HTML entities
        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void SingleLineDocumentation_ReportsCorrectLineNumber()
    {
        var code = "model TestModel \"A simple model\"\n" +
                   "  Real x;\n" +
                   "equation\n" +
                   "  x = 1.0;\n" +
                   "  annotation(Documentation(info=\"<html><p>Has a misspeling.</p></html>\"));\n" +
                   "end TestModel;";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        var finding = visitor.RuleFindings.FirstOrDefault(v => v.Summary.Contains("misspeling"));
        Assert.NotNull(finding);
        // Single-line string on line 5
        Assert.Equal(5, finding.LineNumber);
    }

    [Fact]
    public void WithBasePackage_TracksFQN()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Has a misspeling.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker(), basePackage: "MyLibrary");
        visitor.Visit(parseTree);

        Assert.NotEmpty(visitor.RuleFindings);
        Assert.Contains("MyLibrary", visitor.RuleFindings[0].ModelName);
    }

    // ── Nested class scope stack tests ──

    [Fact]
    public void NestedClass_ComponentNamesInOuterScope_NotVisibleInInner()
    {
        // Outer model has "rflx" variable, nested model should push a new scope
        var code = """
            package MyPkg
              model Outer "Outer model"
                Real rflx;
                model Inner "Inner model"
                  Real y;
                  annotation(Documentation(info="<html><p>The rflx component is referenced.</p></html>"));
                end Inner;
              end Outer;
            end MyPkg;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        // "rflx" is in outer scope — with scope stacking it should still be visible
        var rflxFindings = visitor.RuleFindings
            .Where(v => v.Summary.Contains("'rflx'"))
            .ToList();
        Assert.Empty(rflxFindings);
    }

    [Fact]
    public void DeeplyNestedClasses_ScopeStackManaged()
    {
        // Three levels of nesting — tests push/pop of scope stack
        var code = """
            package Level1 "L1"
              model Level2 "L2"
                Real outerVar;
                model Level3 "L3"
                  Real innerVar;
                  annotation(Documentation(info="<html><p>Uses outerVar and innerVar.</p></html>"));
                end Level3;
              end Level2;
            end Level1;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        // Both outerVar and innerVar should be in context words
        var findings = visitor.RuleFindings
            .Where(v => v.Summary.Contains("outerVar") || v.Summary.Contains("innerVar"))
            .ToList();
        Assert.Empty(findings);
    }

    [Fact]
    public void EmptyDocumentationAnnotation_NoArguments_NoFinding()
    {
        // annotation(Documentation()) — empty argument list
        var code = """
            model TestModel "A simple model"
              Real x;
              annotation(Documentation());
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void DocumentationWithKnownModelNames_NoFalsePositives()
    {
        var knownModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HeatExchanger", "PressureDrop", "FlowModel"
        };

        var code = """
            model TestModel "A simple model"
              Real x;
              annotation(Documentation(info="<html><p>This model extends HeatExchanger and uses PressureDrop and FlowModel.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker(), knownModelNames);
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void ShortClassSpecifier_ScopeStillTracked()
    {
        var code = """
            type MyType = Real;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        // Should not throw — tests that OnClassEntered/OnClassExited work with short class specifier
        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void DerClassSpecifier_ScopeStillTracked()
    {
        var code = """
            type MyDerType = der(Real, x);
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void NoComponentNames_NoModelNames_ContextWordsNull()
    {
        // No components and no known model names — BuildContextWords returns null
        var code = """
            model TestModel "A simple model"
              annotation(Documentation(info="<html><p>Has a misspeling here.</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Contains(visitor.RuleFindings, v => v.Summary.Contains("misspeling"));
    }

    [Fact]
    public void WhitespaceOnlyDocumentation_NoFinding()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
              annotation(Documentation(info="   "));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void HtmlOnlyDocumentation_NoTextContent_NoFinding()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
              annotation(Documentation(info="<html><p></p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void TheFlaggedWordCanBeReadBackFromTheMessage()
    {
        // The code view underlines the word by reading it out of the finding. A documentation
        // finding's Discriminator holds "documentation info:<word>" so its fingerprint is unique,
        // and taking the word from there underlined nothing — no such text is in the source.
        var code = """
            model TestModel
              annotation(Documentation(info="<html><p>The tyre presure is low</p></html>"));
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        var finding = Assert.Single(visitor.Findings);
        Assert.Equal("presure", SpellingMessage.WordFrom(finding.Message));
        Assert.Contains("presure", finding.Discriminator);   // unique per section AND word
    }
}
