using Xunit;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class SpellCheckDescriptionsTests
{
    private static SpellChecker CreateSpellChecker() => SpellChecker.Create();

    [Fact]
    public void CorrectDescription_NoViolations()
    {
        var code = """
            model TestModel "A simple model for testing"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleViolations);
    }

    [Fact]
    public void MisspelledDescription_ReportsViolation()
    {
        var code = """
            model TestModel "A simpl modl for testin"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.NotEmpty(visitor.RuleViolations);
        Assert.All(visitor.RuleViolations, v => Assert.Contains("Misspelled word", v.Summary));
    }

    [Fact]
    public void EmptyDescription_NoViolations()
    {
        var code = """
            model TestModel ""
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleViolations);
    }

    [Fact]
    public void NoDescription_NoViolations()
    {
        var code = """
            model TestModel
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleViolations);
    }

    [Fact]
    public void ModelicaTermsInDescription_NoFalsePositives()
    {
        var code = """
            model TestModel "Uses Modelica linearization and Jacobian computation"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleViolations);
    }

    [Fact]
    public void ModelNameInDescription_NoViolation()
    {
        var knownModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Step", "Integrator", "PID"
        };

        var code = """
            model TestModel "Wraps a Step source and an Integrator"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker(), knownModelNames);
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleViolations);
    }

    [Fact]
    public void ComponentNameInDescription_NoViolation()
    {
        // The component 'myPump' is camelCase so it gets skipped by ShouldSkipWord.
        // But let's test a single-word component name that could look like a misspelling.
        var code = """
            model TestModel "A simple model"
              Real rflx "The rflx value";
              Real other "References the rflx component";
            equation
              rflx = 1.0;
              other = rflx;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        // 'rflx' appears in component declarations and descriptions.
        // The first declaration of 'rflx' adds it to the scope.
        // However, 'rflx' description itself is checked AFTER adding it to scope,
        // so "rflx" in the first component's description should be recognized.
        // The second component references it and should also be fine.
        var rflxViolations = visitor.RuleViolations
            .Where(v => v.Summary.Contains("'rflx'"))
            .ToList();
        Assert.Empty(rflxViolations);
    }

    [Fact]
    public void ComponentDescription_Checked()
    {
        var code = """
            model TestModel "A valid description"
              Real myVar "This has a definite misspeling here";
            equation
              myVar = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Contains(visitor.RuleViolations, v => v.Summary.Contains("misspeling"));
    }

    [Fact]
    public void ShortClassSpecifier_DescriptionChecked()
    {
        var code = """
            type BadType = Real "This has a misspeling";
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Contains(visitor.RuleViolations, v => v.Summary.Contains("misspeling"));
    }

    [Fact]
    public void DerClassSpecifier_DescriptionChecked()
    {
        var code = """
            type BadDer = der(Position, time) "This has a misspeling";
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Contains(visitor.RuleViolations, v => v.Summary.Contains("misspeling"));
    }

    [Fact]
    public void ConcatenatedStrings_AllChecked()
    {
        var code = """
            model TestModel "First part is fine" + "but this has a misspeling"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Contains(visitor.RuleViolations, v => v.Summary.Contains("misspeling"));
    }

    [Fact]
    public void NestedClasses_ScopedComponentNames()
    {
        // Component names from the outer class should not leak into the inner class scope
        // (though per the implementation, all scopes in the stack are checked)
        var code = """
            package TestPkg "Test package"
              model Outer "Outer model"
                Real outerComp "The outerComp value";
                model Inner "Inner model"
                  Real innerComp "References outerComp here too";
                end Inner;
              end Outer;
            end TestPkg;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        // Both outerComp and innerComp should be recognized as valid
        var compViolations = visitor.RuleViolations
            .Where(v => v.Summary.Contains("outerComp") || v.Summary.Contains("innerComp"))
            .ToList();
        Assert.Empty(compViolations);
    }

    [Fact]
    public void WithBasePackage_TracksFQN()
    {
        var code = """
            model TestModel "A misspeling in description"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker(), basePackage: "MyLibrary");
        visitor.Visit(parseTree);

        Assert.NotEmpty(visitor.RuleViolations);
        Assert.Contains("MyLibrary", visitor.RuleViolations[0].ModelName);
    }

    [Fact]
    public void MultiLineDescription_ReportsCorrectLineNumber()
    {
        // Line 1: model TestModel "First line is fine
        // Line 2: but this line has a misspeling"
        var code = "model TestModel \"First line is fine\nbut this line has a misspeling\"\n  Real x;\nequation\n  x = 1.0;\nend TestModel;";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        var violation = visitor.RuleViolations.FirstOrDefault(v => v.Summary.Contains("misspeling"));
        Assert.NotNull(violation);
        // "misspeling" is on the second line of the string, so line 2
        Assert.Equal(2, violation.LineNumber);
    }

    [Fact]
    public void SingleLineDescription_ReportsCorrectLineNumber()
    {
        var code = "model TestModel \"A misspeling here\"\n  Real x;\nequation\n  x = 1.0;\nend TestModel;";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        var violation = visitor.RuleViolations.FirstOrDefault(v => v.Summary.Contains("misspeling"));
        Assert.NotNull(violation);
        // Description is on line 1
        Assert.Equal(1, violation.LineNumber);
    }

    [Fact]
    public void CamelCaseWordsInDescription_Skipped()
    {
        var code = """
            model TestModel "Uses the getValue method for processing"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        // getValue should be skipped by ShouldSkipWord (camelCase)
        var camelViolations = visitor.RuleViolations
            .Where(v => v.Summary.Contains("getValue"))
            .ToList();
        Assert.Empty(camelViolations);
    }
}
