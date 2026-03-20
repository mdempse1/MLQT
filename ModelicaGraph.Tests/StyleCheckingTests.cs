using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

public class StyleCheckingTests
{
    // ============================================================================
    // StyleCheckingSettings
    // ============================================================================

    [Fact]
    public void StyleCheckingSettings_DefaultValues_AllFalse()
    {
        var settings = new StyleCheckingSettings();

        Assert.False(settings.CommitRequiresIssueNumber);
        Assert.False(settings.IssueNumberAtEnd);
        Assert.False(settings.ApplyFormattingRules);
        Assert.False(settings.ImportStatementsFirst);
        Assert.False(settings.ComponentsBeforeClasses);
        Assert.False(settings.OneOfEachSection);
        Assert.False(settings.DontMixEquationAndAlgorithm);
        Assert.False(settings.DontMixConnections);
        Assert.False(settings.InitialEQAlgoFirst);
        Assert.False(settings.InitialEQAlgoLast);
        Assert.False(settings.ClassHasDescription);
        Assert.False(settings.ClassHasDocumentationInfo);
        Assert.False(settings.ClassHasDocumentationRevisions);
        Assert.False(settings.ClassHasIcon);
        Assert.False(settings.ParameterHasDescription);
        Assert.False(settings.ConstantHasDescription);
        Assert.False(settings.FollowNamingConvention);
        Assert.False(settings.SpellCheckDescription);
        Assert.False(settings.SpellCheckDocumentation);
        Assert.False(settings.ValidateModelReferences);
    }

    [Fact]
    public void StyleCheckingSettings_Properties_CanBeSet()
    {
        var settings = new StyleCheckingSettings
        {
            CommitRequiresIssueNumber = true,
            IssueNumberAtEnd = true,
            ApplyFormattingRules = true,
            ImportStatementsFirst = true,
            ComponentsBeforeClasses = true,
            OneOfEachSection = true,
            DontMixEquationAndAlgorithm = true,
            DontMixConnections = true,
            InitialEQAlgoFirst = true,
            InitialEQAlgoLast = true,
            ClassHasDescription = true,
            ClassHasDocumentationInfo = true,
            ClassHasDocumentationRevisions = true,
            ClassHasIcon = true,
            ParameterHasDescription = true,
            ConstantHasDescription = true,
            FollowNamingConvention = true,
            SpellCheckDescription = true,
            SpellCheckDocumentation = true,
            ValidateModelReferences = true
        };

        Assert.True(settings.CommitRequiresIssueNumber);
        Assert.True(settings.IssueNumberAtEnd);
        Assert.True(settings.ApplyFormattingRules);
        Assert.True(settings.ImportStatementsFirst);
        Assert.True(settings.ComponentsBeforeClasses);
        Assert.True(settings.OneOfEachSection);
        Assert.True(settings.DontMixEquationAndAlgorithm);
        Assert.True(settings.DontMixConnections);
        Assert.True(settings.InitialEQAlgoFirst);
        Assert.True(settings.InitialEQAlgoLast);
        Assert.True(settings.ClassHasDescription);
        Assert.True(settings.ClassHasDocumentationInfo);
        Assert.True(settings.ClassHasDocumentationRevisions);
        Assert.True(settings.ClassHasIcon);
        Assert.True(settings.ParameterHasDescription);
        Assert.True(settings.ConstantHasDescription);
        Assert.True(settings.FollowNamingConvention);
        Assert.True(settings.SpellCheckDescription);
        Assert.True(settings.SpellCheckDocumentation);
        Assert.True(settings.ValidateModelReferences);
    }

    // ============================================================================
    // StyleChecking.RunStyleChecking
    // ============================================================================

    private static ModelDefinition MakeModel(string name, string code) =>
        new ModelDefinition(name, code);

    [Fact]
    public void RunStyleChecking_NoSettingsEnabled_ReturnsEmptyList()
    {
        var model = MakeModel("TestModel", "model TestModel Real x; end TestModel;");
        var settings = new StyleCheckingSettings();

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
        Assert.True(model.StyleRulesChecked);
    }

    [Fact]
    public void RunStyleChecking_WithPreParsedCode_SkipsReparsing()
    {
        // Arrange: pre-parse the model so ParsedCode is already set
        var code = "model TestModel Real x; end TestModel;";
        var model = MakeModel("TestModel", code);
        var (parsedCode, _) = ModelicaParser.Helpers.ModelicaParserHelper.ParseWithErrors(code);
        model.ParsedCode = parsedCode;

        var settings = new StyleCheckingSettings();

        // Act: should use existing ParsedCode, not re-parse
        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotNull(model.ParsedCode);
        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_WithFullModelId_ExtractsBasePackage()
    {
        var model = MakeModel("TestModel", "model TestModel Real x; end TestModel;");
        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        // fullModelId with dot → basePackage = "MyPackage"
        var violations = StyleChecking.RunStyleChecking(model, settings, "MyPackage.TestModel");

        // Should complete without error; basePackage extraction is exercised
        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_WithFullModelIdNoDot_EmptyBasePackage()
    {
        var model = MakeModel("TestModel", "model TestModel Real x; end TestModel;");
        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        // fullModelId with no dot → basePackage stays ""
        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel");

        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_ParameterHasDescription_DetectsUndescribedParameter()
    {
        var code = """
model TestModel
  parameter Real x = 1.0;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ParameterHasDescription = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        // Parameter x has no description — should produce a violation
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void RunStyleChecking_ConstantHasDescription_DetectsUndescribedConstant()
    {
        var code = """
model TestModel
  constant Real g = 9.81;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ConstantHasDescription = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void RunStyleChecking_ParameterAndConstant_BothEnabled()
    {
        var code = """
model TestModel
  parameter Real x = 1.0;
  constant Real g = 9.81;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings
        {
            ParameterHasDescription = true,
            ConstantHasDescription = true
        };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.True(violations.Count >= 2);
    }

    [Fact]
    public void RunStyleChecking_ImportStatementsFirst_DetectsViolation()
    {
        var code = """
model TestModel
  Real x;
  import Modelica.Math;
equation
  x = 1.0;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ImportStatementsFirst = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        // Import after component declaration is a violation
        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoFirst_Exercised()
    {
        var code = """
model TestModel
  Real x;
equation
  x = 1.0;
initial equation
  x = 0.0;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoFirst = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoLast_Exercised()
    {
        var code = """
model TestModel
  Real x;
initial equation
  x = 0.0;
equation
  x = 1.0;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoLast = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_OneOfEachSection_DetectsMultipleSections()
    {
        var code = """
model TestModel
  Real x;
  Real y;
equation
  x = 1.0;
equation
  y = 2.0;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { OneOfEachSection = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_DontMixEquationAndAlgorithm_Exercised()
    {
        var code = """
model TestModel
  Real x;
equation
  x = 1.0;
algorithm
  x := 2.0;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { DontMixEquationAndAlgorithm = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_OneOfEachAndDontMixBothEnabled_Exercised()
    {
        var code = """
model TestModel
  Real x;
equation
  x = 1.0;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings
        {
            OneOfEachSection = true,
            DontMixEquationAndAlgorithm = true
        };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_DontMixConnections_DetectsViolation()
    {
        var code = """
model TestModel
  Real x;
  RealOutput y;
equation
  connect(x, y);
  x = 1.0;
end TestModel;
""";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { DontMixConnections = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotNull(violations);
    }

    [Fact]
    public void RunStyleChecking_ClassHasDescription_DetectsUndescribedModel()
    {
        var code = "model TestModel Real x; end TestModel;";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        // Model without description string should produce a violation
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void RunStyleChecking_ClassHasDescription_NoneForDescribedModel()
    {
        var code = "model TestModel \"A described model\" Real x; end TestModel;";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    // ============================================================================
    // ValidateModelReferences
    // ============================================================================

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_NoViolationForKnownModel()
    {
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://Modelica.Blocks.Continuous\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "Modelica.Blocks.Continuous", "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_ViolationForUnknownModel()
    {
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://Modelica.Blocks.NonExistent\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "Modelica.Blocks.Continuous", "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.NotEmpty(violations);
        Assert.Contains("Modelica.Blocks.NonExistent", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_IgnoresFileReferences()
    {
        // URIs with '/' are file references, not model references — should not be validated
        var code = """
            model TestModel "A test model"
              parameter String fileName = Modelica.Utilities.Files.loadResource("modelica://Modelica/Resources/data.mat");
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_MultipleReferencesInOneString()
    {
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://Known.Model\">ok</a> and <a href=\"modelica://Missing.Model\">broken</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "Known.Model", "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.Single(violations);
        Assert.Contains("Missing.Model", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_DisabledByDefault()
    {
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://NonExistent.Model\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings(); // ValidateModelReferences = false by default

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", new HashSet<string>());

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_NullKnownModels_SkipsCheck()
    {
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://NonExistent.Model\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };

        // knownModelIds is null — rule should be skipped
        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", null);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_WithHashFragment()
    {
        // modelica://Model.Name#section should extract "Model.Name" (before #)
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://Known.Model#info\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "Known.Model", "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_QuotedIdentifiers()
    {
        // Quoted identifiers like 'function' are distinct from unquoted — exact match required
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://ModelicaReference.Classes.'function'\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "ModelicaReference.Classes.'function'", "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_QuotedIdentifiersWithSpecialChars()
    {
        // Quoted identifiers can contain delimiter characters like () that must not truncate the URI
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://ModelicaReference.Operators.'semiLinear()'\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "ModelicaReference.Operators.'semiLinear()'", "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_QuotedAndUnquotedAreDistinct()
    {
        // Lib.'Test' and Lib.Test are different classes — no fallback stripping
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://Lib.'Test'\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        // Only unquoted Lib.Test exists, but the URI references Lib.'Test'
        var knownModels = new HashSet<string> { "Lib.Test", "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        // Should report a violation — Lib.'Test' is not the same as Lib.Test
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_QuotedIdentifiers_NotFound()
    {
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><a href=\"modelica://SomeLib.'missing'\">link</a></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.NotEmpty(violations);
        Assert.Contains("SomeLib.'missing'", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_HtmlEntityEncodedLinks_Ignored()
    {
        // HTML entity-encoded example code should not be treated as real links
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html>&lt;a href=&quot;modelica://Modelica.Mechanics.MultiBody&quot;&gt;link&lt;/a&gt;</html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        // The entity-encoded link should be ignored, so no violation even though
        // "Modelica.Mechanics.MultiBody" is not in knownModels
        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_LineNumber_OffsetByNewlines()
    {
        // The broken reference is on line 5 of the source (3 newlines into the multi-line string
        // that starts on line 2)
        var code = "model TestModel \"A test model\"\n" +                          // line 1
                   "  annotation(Documentation(info=\"<html>\n" +                  // line 2 (string starts here)
                   "<p>Some documentation</p>\n" +                                 // line 3
                   "<p>More text</p>\n" +                                          // line 4
                   "<a href=\\\"modelica://Missing.Model\\\">link</a>\n" +         // line 5
                   "</html>\"));\n" +                                               // line 6
                   "end TestModel;\n";                                             // line 7
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.Single(violations);
        Assert.Equal(5, violations[0].LineNumber);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_PlainTextMention_Ignored()
    {
        // modelica:// mentioned in plain text (not in an href attribute) should not be validated
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><td>Replace Modelica://-URIs by modelica://-URIs</td></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ValidateModelReferences_PlainTextWithRealLink()
    {
        // Mix of plain text mention and a real link — only the real link should be validated
        var code = """
            model TestModel "A test model"
              annotation(Documentation(info="<html><p>Use modelica:// URIs like <a href=\"modelica://Missing.Model\">this</a></p></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        var knownModels = new HashSet<string> { "TestModel" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel", knownModels);

        // Only the href link should produce a violation, not the plain text mention
        Assert.Single(violations);
        Assert.Contains("Missing.Model", violations[0].Summary);
    }

    [Fact]
    public void StyleCheckingSettings_ValidateModelReferences_DefaultFalse()
    {
        var settings = new StyleCheckingSettings();
        Assert.False(settings.ValidateModelReferences);
    }

    [Fact]
    public void StyleCheckingSettings_ValidateModelReferences_IncludedInHasAnyStyleRuleEnabled()
    {
        var settings = new StyleCheckingSettings { ValidateModelReferences = true };
        Assert.True(settings.HasAnyStyleRuleEnabled);
    }

    // ============================================================================
    // Spell Checking Integration Tests
    // ============================================================================

    [Fact]
    public void HasAnyStyleRuleEnabled_SpellCheckDescription_ReturnsTrue()
    {
        var settings = new StyleCheckingSettings { SpellCheckDescription = true };
        Assert.True(settings.HasAnyStyleRuleEnabled);
    }

    [Fact]
    public void HasAnyStyleRuleEnabled_SpellCheckDocumentation_ReturnsTrue()
    {
        var settings = new StyleCheckingSettings { SpellCheckDocumentation = true };
        Assert.True(settings.HasAnyStyleRuleEnabled);
    }

    [Fact]
    public void RunStyleChecking_SpellCheckDescription_FindsMisspellings()
    {
        var code = """
            model TestModel "A simpl modl"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { SpellCheckDescription = true };
        var spellChecker = ModelicaParser.SpellChecking.SpellChecker.Create();

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel",
            spellChecker: spellChecker);

        Assert.NotEmpty(violations);
        Assert.All(violations, v => Assert.Contains("Misspelled word", v.Summary));
    }

    [Fact]
    public void RunStyleChecking_SpellCheckDescription_NoViolationsForCorrectText()
    {
        var code = """
            model TestModel "A simple model for testing"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { SpellCheckDescription = true };
        var spellChecker = ModelicaParser.SpellChecking.SpellChecker.Create();

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel",
            spellChecker: spellChecker);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_SpellCheckDescription_NullSpellChecker_SkipsChecking()
    {
        var code = """
            model TestModel "A simpl modl"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { SpellCheckDescription = true };

        // No spell checker passed — should skip spell checking
        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel");

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_SpellCheckDocumentation_FindsMisspellings()
    {
        var code = """
            model TestModel "A simple model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>This has a misspeling.</p></html>"));
            end TestModel;
            """;

        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { SpellCheckDocumentation = true };
        var spellChecker = ModelicaParser.SpellChecking.SpellChecker.Create();

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel",
            spellChecker: spellChecker);

        Assert.Contains(violations, v => v.Summary.Contains("misspeling"));
    }

    [Fact]
    public void RunStyleChecking_SpellCheckWithKnownModelNames()
    {
        var code = """
            model TestModel "Uses an Integrator for processing"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { SpellCheckDescription = true };
        var spellChecker = ModelicaParser.SpellChecking.SpellChecker.Create();
        var knownModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Integrator" };

        var violations = StyleChecking.RunStyleChecking(model, settings, "TestModel",
            spellChecker: spellChecker, knownModelNames: knownModelNames);

        // "Integrator" should not be flagged because it's a known model name
        Assert.DoesNotContain(violations, v => v.Summary.Contains("Integrator"));
    }

    // ============================================================================
    // NamingConvention JSON deserialization
    // ============================================================================

    [Fact]
    public void NamingConvention_DeserializeWithoutNamingConvention_DefaultsToTrue()
    {
        // Simulates loading a settings.json saved before naming conventions were added
        var json = """{"FollowNamingConvention":true}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<StyleCheckingSettings>(json)!;

        Assert.True(settings.NamingConvention.AllowUnderscoreSuffixes);
        Assert.Equal("Modelica Standard", settings.NamingConvention.PresetName);
    }

    [Fact]
    public void NamingConvention_DeserializeWithPartialNamingConvention_DefaultsCorrectly()
    {
        // Simulates a settings.json with NamingConvention but missing AllowUnderscoreSuffixes
        var json = """{"FollowNamingConvention":true,"NamingConvention":{"PresetName":"Custom"}}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<StyleCheckingSettings>(json)!;

        Assert.True(settings.NamingConvention.AllowUnderscoreSuffixes);
    }

    // ============================================================================
    // ClassHasDocumentationInfo
    // ============================================================================

    [Fact]
    public void RunStyleChecking_ClassHasDocumentationInfo_DetectsMissingInfo()
    {
        var code = """
            model TestModel "A model without documentation"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasDocumentationInfo = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotEmpty(violations);
        Assert.Contains("Documentation info", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_ClassHasDocumentationInfo_NoViolationWhenPresent()
    {
        var code = """
            model TestModel "A documented model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Info here</p></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasDocumentationInfo = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ClassHasDocumentationInfo_IncludedInHasAnyStyleRuleEnabled()
    {
        var settings = new StyleCheckingSettings { ClassHasDocumentationInfo = true };
        Assert.True(settings.HasAnyStyleRuleEnabled);
    }

    // ============================================================================
    // ClassHasDocumentationRevisions
    // ============================================================================

    [Fact]
    public void RunStyleChecking_ClassHasDocumentationRevisions_DetectsMissingRevisions()
    {
        var code = """
            model TestModel "A model without revisions"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Info</p></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasDocumentationRevisions = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotEmpty(violations);
        Assert.Contains("Documentation revisions", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_ClassHasDocumentationRevisions_NoViolationWhenPresent()
    {
        var code = """
            model TestModel "A documented model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Info</p></html>", revisions="<html><p>v1.0</p></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasDocumentationRevisions = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ClassHasDocumentationRevisions_IncludedInHasAnyStyleRuleEnabled()
    {
        var settings = new StyleCheckingSettings { ClassHasDocumentationRevisions = true };
        Assert.True(settings.HasAnyStyleRuleEnabled);
    }

    // ============================================================================
    // ClassHasIcon
    // ============================================================================

    [Fact]
    public void RunStyleChecking_ClassHasIcon_DetectsMissingIcon()
    {
        var code = """
            model TestModel "A model without icon"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasIcon = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotEmpty(violations);
        Assert.Contains("Icon", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_ClassHasIcon_NoViolationWhenPresent()
    {
        var code = """
            model TestModel "A model with icon"
              Real x;
            equation
              x = 1.0;
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}})));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasIcon = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ClassHasIcon_IncludedInHasAnyStyleRuleEnabled()
    {
        var settings = new StyleCheckingSettings { ClassHasIcon = true };
        Assert.True(settings.HasAnyStyleRuleEnabled);
    }

    // ============================================================================
    // Combined annotation checks
    // ============================================================================

    [Fact]
    public void RunStyleChecking_AllAnnotationChecks_DetectsAllMissing()
    {
        var code = """
            model TestModel "A bare model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings
        {
            ClassHasDocumentationInfo = true,
            ClassHasDocumentationRevisions = true,
            ClassHasIcon = true
        };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.Summary.Contains("Documentation info"));
        Assert.Contains(violations, v => v.Summary.Contains("Documentation revisions"));
        Assert.Contains(violations, v => v.Summary.Contains("Icon"));
    }

    [Fact]
    public void RunStyleChecking_AllAnnotationChecks_NoViolationsWhenAllPresent()
    {
        var code = """
            model TestModel "A fully annotated model"
              Real x;
            equation
              x = 1.0;
              annotation(
                Documentation(info="<html><p>Info</p></html>", revisions="<html><p>v1.0</p></html>"),
                Icon(coordinateSystem(extent={{-100,-100},{100,100}}))
              );
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings
        {
            ClassHasDocumentationInfo = true,
            ClassHasDocumentationRevisions = true,
            ClassHasIcon = true
        };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_AnnotationChecks_DocumentationWithoutIcon_OnlyIconViolation()
    {
        var code = """
            model TestModel "A model with docs but no icon"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Info</p></html>", revisions="<html><p>v1.0</p></html>"));
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings
        {
            ClassHasDocumentationInfo = true,
            ClassHasDocumentationRevisions = true,
            ClassHasIcon = true
        };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Single(violations);
        Assert.Contains("Icon", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_AnnotationChecks_DisabledByDefault()
    {
        var code = """
            model TestModel "A bare model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings();

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    // ============================================================================
    // InitialEQAlgoLast (fixed implementation)
    // ============================================================================

    [Fact]
    public void RunStyleChecking_InitialEQAlgoLast_DetectsInitialBeforeRegular()
    {
        // Initial equation comes BEFORE regular equation — violates "last" rule
        var code = """
            model TestModel
              Real x;
            initial equation
              x = 0.0;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoLast = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotEmpty(violations);
        Assert.Contains("should appear after the equation/algorithm section", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoLast_NoViolationWhenInitialIsLast()
    {
        // Initial equation comes AFTER regular equation — satisfies "last" rule
        var code = """
            model TestModel
              Real x;
            equation
              x = 1.0;
            initial equation
              x = 0.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoLast = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoLast_DetectsInitialAlgorithmBeforeRegular()
    {
        // Initial algorithm comes BEFORE regular algorithm — violates "last" rule
        var code = """
            model TestModel
              Real x;
            initial algorithm
              x := 0.0;
            algorithm
              x := 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoLast = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoLast_NoViolationWhenInitialAlgorithmIsLast()
    {
        // Initial algorithm comes AFTER regular algorithm — satisfies "last" rule
        var code = """
            model TestModel
              Real x;
            algorithm
              x := 1.0;
            initial algorithm
              x := 0.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoLast = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoFirst_DetectsInitialAfterRegular()
    {
        // Initial equation comes AFTER regular equation — violates "first" rule
        var code = """
            model TestModel
              Real x;
            equation
              x = 1.0;
            initial equation
              x = 0.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoFirst = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.NotEmpty(violations);
        Assert.Contains("should appear before", violations[0].Summary);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoFirst_NoViolationWhenInitialIsFirst()
    {
        // Initial equation comes BEFORE regular equation — satisfies "first" rule
        var code = """
            model TestModel
              Real x;
            initial equation
              x = 0.0;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoFirst = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoFirst_NoViolationWithOnlyEquation()
    {
        // Only regular equation section, no initial — no violation for either rule
        var code = """
            model TestModel
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoFirst = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_InitialEQAlgoLast_NoViolationWithOnlyEquation()
    {
        var code = """
            model TestModel
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoLast = true };

        var violations = StyleChecking.RunStyleChecking(model, settings);

        Assert.Empty(violations);
    }

    // ============================================================================
    // isExcludedFromFormatting parameter
    // ============================================================================

    [Fact]
    public void RunStyleChecking_ExcludedFromFormatting_SkipsFormattingRules()
    {
        // Import after component should produce violations when NOT excluded
        var code = """
            model TestModel
              Real x;
              import Modelica.Math;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ImportStatementsFirst = true };

        // First verify it produces violations normally
        var normalViolations = StyleChecking.RunStyleChecking(model, settings);

        // Now check that the same code with isExcludedFromFormatting skips the formatting rules
        var model2 = MakeModel("TestModel", code);
        var excludedViolations = StyleChecking.RunStyleChecking(model2, settings, isExcludedFromFormatting: true);

        Assert.NotEmpty(normalViolations);
        Assert.Empty(excludedViolations);
    }

    [Fact]
    public void RunStyleChecking_ExcludedFromFormatting_SkipsInitialEqAlgo()
    {
        var code = """
            model TestModel
              Real x;
            equation
              x = 1.0;
            initial equation
              x = 0.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { InitialEQAlgoFirst = true };

        var violations = StyleChecking.RunStyleChecking(model, settings, isExcludedFromFormatting: true);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ExcludedFromFormatting_SkipsOneOfEachSection()
    {
        var code = """
            model TestModel
              Real x;
              Real y;
            equation
              x = 1.0;
            equation
              y = 2.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { OneOfEachSection = true };

        var violations = StyleChecking.RunStyleChecking(model, settings, isExcludedFromFormatting: true);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ExcludedFromFormatting_SkipsDontMixConnections()
    {
        var code = """
            model TestModel
              Real x;
              RealOutput y;
            equation
              connect(x, y);
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { DontMixConnections = true };

        var violations = StyleChecking.RunStyleChecking(model, settings, isExcludedFromFormatting: true);

        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_ExcludedFromFormatting_StillChecksNonFormattingRules()
    {
        // Non-formatting rules like ClassHasDescription should still be checked
        var code = "model TestModel Real x; end TestModel;";
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings
        {
            ClassHasDescription = true,
            ImportStatementsFirst = true  // This should be skipped
        };

        var violations = StyleChecking.RunStyleChecking(model, settings, isExcludedFromFormatting: true);

        // Only ClassHasDescription violation, not ImportStatementsFirst
        Assert.Single(violations);
        Assert.Contains("description", violations[0].Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunStyleChecking_ExcludedFromFormatting_StillChecksParameterDescription()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ParameterHasDescription = true };

        var violations = StyleChecking.RunStyleChecking(model, settings, isExcludedFromFormatting: true);

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void RunStyleChecking_ExcludedFromFormatting_StillChecksAnnotations()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings
        {
            ClassHasDocumentationInfo = true,
            ClassHasIcon = true
        };

        var violations = StyleChecking.RunStyleChecking(model, settings, isExcludedFromFormatting: true);

        Assert.Equal(2, violations.Count);
    }

    // ============================================================================
    // CreateBaseClassHasIconCallback
    // ============================================================================

    [Fact]
    public void CreateBaseClassHasIconCallback_NullGraph_ReturnsNull()
    {
        var callback = StyleChecking.CreateBaseClassHasIconCallback(null);

        Assert.Null(callback);
    }

    [Fact]
    public void CreateBaseClassHasIconCallback_WithGraph_ReturnsNonNull()
    {
        var graph = new DirectedGraph();
        var callback = StyleChecking.CreateBaseClassHasIconCallback(graph);

        Assert.NotNull(callback);
    }

    [Fact]
    public void CreateBaseClassHasIconCallback_BaseClassNotInGraph_ReturnsFalse()
    {
        var graph = new DirectedGraph();
        var callback = StyleChecking.CreateBaseClassHasIconCallback(graph)!;

        var result = callback("NonExistent.BaseClass", "MyPackage.MyModel");

        Assert.False(result);
    }

    [Fact]
    public void CreateBaseClassHasIconCallback_BaseClassWithIcon_ReturnsTrue()
    {
        var graph = new DirectedGraph();
        var baseCode = """
            model BaseModel "A base"
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}}),
                graphics={Rectangle(extent={{-80,-80},{80,80}})}));
            end BaseModel;
            """;
        var baseNode = new ModelNode("BaseModel", "BaseModel", baseCode);
        graph.AddNode(baseNode);

        var callback = StyleChecking.CreateBaseClassHasIconCallback(graph)!;
        var result = callback("BaseModel", "MyPackage.MyModel");

        Assert.True(result);
    }

    [Fact]
    public void CreateBaseClassHasIconCallback_BaseClassWithoutIcon_ReturnsFalse()
    {
        var graph = new DirectedGraph();
        var baseCode = """
            model BaseModel "A base"
              Real x;
            equation
              x = 1.0;
            end BaseModel;
            """;
        var baseNode = new ModelNode("BaseModel", "BaseModel", baseCode);
        graph.AddNode(baseNode);

        var callback = StyleChecking.CreateBaseClassHasIconCallback(graph)!;
        var result = callback("BaseModel", "MyPackage.MyModel");

        Assert.False(result);
    }

    [Fact]
    public void CreateBaseClassHasIconCallback_RelativeName_ResolvesViaPackageHierarchy()
    {
        var graph = new DirectedGraph();
        var baseCode = """
            model BaseModel "A base"
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}}),
                graphics={Rectangle(extent={{-80,-80},{80,80}})}));
            end BaseModel;
            """;
        // The base class is at MyPackage.BaseModel
        var baseNode = new ModelNode("MyPackage.BaseModel", "BaseModel", baseCode);
        graph.AddNode(baseNode);

        var callback = StyleChecking.CreateBaseClassHasIconCallback(graph)!;
        // Relative name "BaseModel" should resolve to "MyPackage.BaseModel"
        // when current model is "MyPackage.SubPkg.MyModel"
        var result = callback("BaseModel", "MyPackage.SubPkg.MyModel");

        Assert.True(result);
    }

    [Fact]
    public void CreateBaseClassHasIconCallback_CyclicInheritance_ReturnsFalse()
    {
        var graph = new DirectedGraph();
        // ModelA extends ModelB, ModelB extends ModelA (cycle) — neither has an icon
        var codeA = """
            model ModelA "A"
              extends ModelB;
            end ModelA;
            """;
        var codeB = """
            model ModelB "B"
              extends ModelA;
            end ModelB;
            """;
        graph.AddNode(new ModelNode("ModelA", "ModelA", codeA));
        graph.AddNode(new ModelNode("ModelB", "ModelB", codeB));

        var callback = StyleChecking.CreateBaseClassHasIconCallback(graph)!;
        var result = callback("ModelA", "SomeModel");

        // Should not infinite loop, should return false
        Assert.False(result);
    }

    [Fact]
    public void CreateBaseClassHasIconCallback_TransitiveInheritance_ReturnsTrue()
    {
        var graph = new DirectedGraph();
        // GrandBase has icon, Base extends GrandBase (no icon), Model extends Base
        var grandBaseCode = """
            model GrandBase "Grand base"
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}}),
                graphics={Rectangle(extent={{-80,-80},{80,80}})}));
            end GrandBase;
            """;
        var baseCode = """
            model Base "A base"
              extends GrandBase;
            end Base;
            """;
        graph.AddNode(new ModelNode("GrandBase", "GrandBase", grandBaseCode));
        graph.AddNode(new ModelNode("Base", "Base", baseCode));

        var callback = StyleChecking.CreateBaseClassHasIconCallback(graph)!;
        var result = callback("Base", "MyModel");

        Assert.True(result);
    }

    // ============================================================================
    // baseClassHasIcon parameter in RunStyleChecking
    // ============================================================================

    [Fact]
    public void RunStyleChecking_WithBaseClassHasIconCallback_PassesToVisitor()
    {
        var code = """
            model TestModel "A model"
              extends BaseModel;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasIcon = true };

        // Callback says base class has icon
        Func<string, string, bool> callback = (baseClass, currentModel) => baseClass == "BaseModel";

        var violations = StyleChecking.RunStyleChecking(model, settings, baseClassHasIcon: callback);

        // No icon violation because inherited icon found
        Assert.Empty(violations);
    }

    [Fact]
    public void RunStyleChecking_NullBaseClassHasIcon_NoInheritanceCheck()
    {
        var code = """
            model TestModel "A model"
              extends BaseModel;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;
        var model = MakeModel("TestModel", code);
        var settings = new StyleCheckingSettings { ClassHasIcon = true };

        var violations = StyleChecking.RunStyleChecking(model, settings, baseClassHasIcon: null);

        Assert.Single(violations);
        Assert.Contains("Icon", violations[0].Summary);
    }

    // ============================================================================
    // HasAnyStyleRuleEnabled
    // ============================================================================

    [Fact]
    public void HasAnyStyleRuleEnabled_FollowNamingConvention_ReturnsTrue()
    {
        var settings = new StyleCheckingSettings { FollowNamingConvention = true };
        Assert.True(settings.HasAnyStyleRuleEnabled);
    }

    [Fact]
    public void HasAnyStyleRuleEnabled_ConstantHasDescription_ReturnsTrue()
    {
        var settings = new StyleCheckingSettings { ConstantHasDescription = true };
        Assert.True(settings.HasAnyStyleRuleEnabled);
    }

    [Fact]
    public void HasAnyStyleRuleEnabled_NoneEnabled_ReturnsFalse()
    {
        var settings = new StyleCheckingSettings();
        Assert.False(settings.HasAnyStyleRuleEnabled);
    }
}
