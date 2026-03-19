using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class FollowNamingConventionTests
{
    private static NamingConventionConfig ModelicaStandardConfig() => new()
    {
        ClassNamingRules = new Dictionary<string, NamingStyle>
        {
            ["model"] = NamingStyle.PascalCase,
            ["function"] = NamingStyle.CamelCase,
            ["block"] = NamingStyle.PascalCase,
            ["connector"] = NamingStyle.PascalCase,
            ["record"] = NamingStyle.PascalCase,
            ["type"] = NamingStyle.PascalCase,
            ["package"] = NamingStyle.PascalCase,
            ["class"] = NamingStyle.PascalCase,
            ["operator"] = NamingStyle.PascalCase
        },
        PublicVariableNaming = NamingStyle.CamelCase,
        PublicParameterNaming = NamingStyle.CamelCase,
        PublicConstantNaming = NamingStyle.CamelCase,
        ProtectedVariableNaming = NamingStyle.CamelCase,
        ProtectedParameterNaming = NamingStyle.CamelCase,
        ProtectedConstantNaming = NamingStyle.CamelCase,
        AllowUnderscoreSuffixes = true
    };

    private List<LogMessage> CheckRule(string code, NamingConventionConfig? config = null, string basePackage = "")
    {
        config ??= ModelicaStandardConfig();
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new FollowNamingConvention(config, basePackage);
        visitor.Visit(parseTree);
        return visitor.RuleViolations;
    }

    // ── Class name checks ──

    [Fact]
    public void PascalCaseModelName_NoViolation()
    {
        var code = """
            model SimpleModel
              Real x;
            end SimpleModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void CamelCaseModelName_Violation()
    {
        var code = """
            model simpleModel
              Real x;
            end simpleModel;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("'simpleModel'", violations[0].Summary);
        Assert.Contains("PascalCase", violations[0].Summary);
        Assert.Contains("model", violations[0].Summary);
    }

    [Fact]
    public void ClassNameViolation_ReportsCorrectModelName()
    {
        // Nested classes are checked independently via their own ModelNode.
        // Simulate checking badModel as a standalone class with basePackage.
        var code = """
            model badModel
              Real x;
            end badModel;
            """;
        var violations = CheckRule(code, basePackage: "MyPackage");
        Assert.Single(violations);
        Assert.Equal("MyPackage.badModel", violations[0].ModelName);
    }

    [Fact]
    public void CamelCaseFunctionName_NoViolation()
    {
        var code = """
            function myFunction
              input Real x;
              output Real y;
            algorithm
              y := x;
            end myFunction;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void PascalCaseFunctionName_Violation()
    {
        var code = """
            function MyFunction
              input Real x;
              output Real y;
            algorithm
              y := x;
            end MyFunction;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("'MyFunction'", violations[0].Summary);
        Assert.Contains("camelCase", violations[0].Summary);
    }

    [Fact]
    public void PascalCaseBlockName_NoViolation()
    {
        var code = """
            block MyBlock
              Real x;
            end MyBlock;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void PascalCaseConnectorName_NoViolation()
    {
        var code = """
            connector MyConnector
              Real p;
              flow Real m_flow;
            end MyConnector;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void PascalCaseRecordName_NoViolation()
    {
        var code = """
            record MyRecord
              Real x;
            end MyRecord;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void PascalCaseTypeName_NoViolation()
    {
        var code = """
            type MyType = Real;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void PascalCasePackageName_NoViolation()
    {
        var code = """
            package MyPackage
            end MyPackage;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void ClassNameWithSuffix_NoViolation()
    {
        var code = """
            model HeatExchanger_simple
              Real x;
            end HeatExchanger_simple;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void SingleCharClassName_NoViolation()
    {
        var code = """
            model T
              Real x;
            end T;
            """;
        Assert.Empty(CheckRule(code));
    }

    // ── Element name checks ──

    [Fact]
    public void CamelCaseVariable_NoViolation()
    {
        var code = """
            model MyModel
              Real myVariable;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void PascalCaseVariable_Violation()
    {
        var code = """
            model MyModel
              Real MyVariable;
            end MyModel;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("'MyVariable'", violations[0].Summary);
        Assert.Contains("camelCase", violations[0].Summary);
        Assert.Contains("public variable", violations[0].Summary);
    }

    [Fact]
    public void CamelCaseParameter_NoViolation()
    {
        var code = """
            model MyModel
              parameter Real myParam = 1.0;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void PascalCaseParameter_Violation()
    {
        var code = """
            model MyModel
              parameter Real MyParam = 1.0;
            end MyModel;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("'MyParam'", violations[0].Summary);
        Assert.Contains("public parameter", violations[0].Summary);
    }

    [Fact]
    public void CamelCaseConstant_NoViolation()
    {
        var code = """
            model MyModel
              constant Real myConst = 3.14;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void SingleCharVariable_NoViolation()
    {
        var code = """
            model MyModel
              Real x;
              Real T;
              Real p;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    // ── Protected elements ──

    [Fact]
    public void ProtectedVariable_UsesProtectedRule()
    {
        var config = ModelicaStandardConfig();
        config = config with { ProtectedVariableNaming = NamingStyle.PascalCase };

        var code = """
            model MyModel
            protected
              Real myProtectedVar;
            end MyModel;
            """;
        var violations = CheckRule(code, config);
        Assert.Single(violations);
        Assert.Contains("protected variable", violations[0].Summary);
    }

    [Fact]
    public void ProtectedVariable_DefaultCamelCase_NoViolation()
    {
        var code = """
            model MyModel
            protected
              Real myProtectedVar;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    // ── Suffix handling ──

    [Fact]
    public void VariableWithSuffix_AllowSuffixes_NoViolation()
    {
        var code = """
            model MyModel
              Real pressure_in;
              Real temperature_a;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void VariableWithSuffix_DisallowSuffixes_Violation()
    {
        var config = ModelicaStandardConfig();
        config = config with { AllowUnderscoreSuffixes = false };

        var code = """
            model MyModel
              Real pressure_in;
            end MyModel;
            """;
        var violations = CheckRule(code, config);
        Assert.Single(violations);
    }

    // ── Nested classes ──

    [Fact]
    public void NestedClasses_SkippedByParentVisitor()
    {
        // Nested classes are checked independently via their own ModelNode
        var code = """
            package MyPackage
              model myBadModel
                Real x;
              end myBadModel;
              model GoodModel
                Real y;
              end GoodModel;
            end MyPackage;
            """;
        var violations = CheckRule(code);
        // MyPackage follows PascalCase; nested models are skipped
        Assert.Empty(violations);
    }

    [Fact]
    public void NestedClass_CheckedViaOwnModelNode()
    {
        // Simulate checking a nested model independently with basePackage
        var code = """
            model myBadModel
              Real x;
            end myBadModel;
            """;
        var violations = CheckRule(code, basePackage: "MyPackage");
        Assert.Single(violations);
        Assert.Contains("'myBadModel'", violations[0].Summary);
    }

    // ── UPPER_CASE constants ──

    [Fact]
    public void UpperCaseConstant_WhenConfigured_NoViolation()
    {
        var config = ModelicaStandardConfig();
        config = config with { PublicConstantNaming = NamingStyle.UpperCase };

        var code = """
            model MyModel
              constant Real MY_CONST = 3.14;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    [Fact]
    public void CamelCaseConstant_WhenUpperCaseConfigured_Violation()
    {
        var config = ModelicaStandardConfig();
        config = config with { PublicConstantNaming = NamingStyle.UpperCase };

        var code = """
            model MyModel
              constant Real myConst = 3.14;
            end MyModel;
            """;
        var violations = CheckRule(code, config);
        Assert.Single(violations);
        Assert.Contains("UPPER_CASE", violations[0].Summary);
    }

    // ── NamingStyle.Any ──

    [Fact]
    public void AnyStyle_NeverReportsViolations()
    {
        var config = new NamingConventionConfig
        {
            ClassNamingRules = new Dictionary<string, NamingStyle>
            {
                ["model"] = NamingStyle.Any
            },
            PublicVariableNaming = NamingStyle.Any,
            PublicParameterNaming = NamingStyle.Any,
            PublicConstantNaming = NamingStyle.Any,
            ProtectedVariableNaming = NamingStyle.Any,
            ProtectedParameterNaming = NamingStyle.Any,
            ProtectedConstantNaming = NamingStyle.Any
        };

        var code = """
            model any_Name_Goes
              Real ANY_thing;
              parameter Real what_Ever = 1;
              constant Real MixedUp = 2;
            end any_Name_Goes;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    // ── Multiple components in one declaration ──

    [Fact]
    public void MultipleComponentsInDeclaration_EachChecked()
    {
        var code = """
            model MyModel
              Real goodVar, BadVar;
            end MyModel;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("'BadVar'", violations[0].Summary);
    }

    // ── Short class specifier ──

    [Fact]
    public void ShortClassSpecifier_NameChecked()
    {
        var code = """
            type myBadType = Real;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("'myBadType'", violations[0].Summary);
        Assert.Contains("PascalCase", violations[0].Summary);
    }

    // ── Public after protected restores visibility ──

    [Fact]
    public void PublicAfterProtected_RestoresVisibility()
    {
        var config = ModelicaStandardConfig();
        config = config with
        {
            PublicVariableNaming = NamingStyle.CamelCase,
            ProtectedVariableNaming = NamingStyle.PascalCase
        };

        var code = """
            model MyModel
              Real camelVar;
            protected
              Real PascalVar;
            public
              Real anotherCamelVar;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    // ── Connector with flow variable ──

    [Fact]
    public void ConnectorFlowVariable_Checked()
    {
        var code = """
            connector MyConnector
              Real p;
              flow Real m_flow;
            end MyConnector;
            """;
        // m_flow should be valid with suffix stripping (base "m" is single char → valid)
        Assert.Empty(CheckRule(code));
    }

    // ── Short abbreviations ──

    [Fact]
    public void ShortAbbreviationVariable_NoViolation()
    {
        var code = """
            model MyModel
              Real P3;
              Real V12;
              Real T2;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void ShortAbbreviationConstant_NoViolation()
    {
        var code = """
            model MyModel
              constant Real T2 = 300;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void SingleCharBaseWithNumericSuffix_NoViolation()
    {
        var code = """
            model MyModel
              Real r_0;
              Real v_rec;
              Real T_start;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    // ── Exception names ──

    [Fact]
    public void ExceptionName_Variable_NoViolation()
    {
        var config = ModelicaStandardConfig();
        config = config with { ExceptionNames = new HashSet<string> { "NASCAR", "OMC" } };

        var code = """
            model MyModel
              Real NASCAR;
              Real OMC;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    [Fact]
    public void ExceptionName_ClassName_NoViolation()
    {
        var config = ModelicaStandardConfig();
        config = config with { ExceptionNames = new HashSet<string> { "mySpecialModel" } };

        var code = """
            model mySpecialModel
              Real x;
            end mySpecialModel;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    [Fact]
    public void ExceptionName_CaseSensitive()
    {
        var config = ModelicaStandardConfig();
        config = config with { ExceptionNames = new HashSet<string> { "NASCAR" } };

        var code = """
            model MyModel
              Real Nascar;
            end MyModel;
            """;
        // "Nascar" is not in exceptions (case-sensitive), so it should violate camelCase
        var violations = CheckRule(code, config);
        Assert.Single(violations);
        Assert.Contains("'Nascar'", violations[0].Summary);
    }

    // ── Array subscripts ──

    [Fact]
    public void ArrayVariable_SubscriptStripped_NoViolation()
    {
        var code = """
            model MyModel
              Real myVector[3];
              parameter Integer myParam[2, 3] = {{1, 2, 3}, {4, 5, 6}};
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void ArrayVariable_BadName_Violation()
    {
        var code = """
            model MyModel
              Real BadVector[3];
            end MyModel;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("'BadVector'", violations[0].Summary);
    }

    // ── Quoted identifiers ──

    [Fact]
    public void QuotedIdentifier_SingleChar_NoViolation()
    {
        var code = """
            model MyModel
              Real 'r';
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void QuotedIdentifier_WithSuffix_NoViolation()
    {
        var code = """
            model MyModel
              Real 'r_0';
              Real 'v_rec';
              Real 'T_start';
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void QuotedIdentifier_CamelCase_NoViolation()
    {
        var code = """
            model MyModel
              Real 'myVariable';
            end MyModel;
            """;
        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void QuotedIdentifier_BadName_Violation()
    {
        var code = """
            model MyModel
              Real 'BadVariable';
            end MyModel;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("BadVariable", violations[0].Summary);
    }

    // ── Der class specifier ──

    [Fact]
    public void DerClassSpecifier_NameChecked()
    {
        var code = """
            type badDerType = der(Real, x);
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.Contains("'badDerType'", violations[0].Summary);
        Assert.Contains("PascalCase", violations[0].Summary);
    }

    [Fact]
    public void DerClassSpecifier_ValidName_NoViolation()
    {
        var code = """
            type GoodDerType = der(Real, x);
            """;
        Assert.Empty(CheckRule(code));
    }

    // ── Class type not in naming rules ──

    [Fact]
    public void ClassTypeNotInRules_NoViolation()
    {
        // Use a config that doesn't have "operator" in class naming rules
        var config = new NamingConventionConfig
        {
            ClassNamingRules = new Dictionary<string, NamingStyle>
            {
                ["model"] = NamingStyle.PascalCase
                // "operator" not included
            },
            PublicVariableNaming = NamingStyle.CamelCase
        };

        var code = """
            operator badOperator
              Real x;
            end badOperator;
            """;
        // No rule for "operator" class type, so no violation
        Assert.Empty(CheckRule(code, config));
    }

    // ── Protected parameter and constant ──

    [Fact]
    public void ProtectedParameter_UsesProtectedRule()
    {
        var config = ModelicaStandardConfig();
        config = config with { ProtectedParameterNaming = NamingStyle.PascalCase };

        var code = """
            model MyModel
            protected
              parameter Real badParam = 1.0;
            end MyModel;
            """;
        var violations = CheckRule(code, config);
        Assert.Single(violations);
        Assert.Contains("protected parameter", violations[0].Summary);
    }

    [Fact]
    public void ProtectedConstant_UsesProtectedRule()
    {
        var config = ModelicaStandardConfig();
        config = config with { ProtectedConstantNaming = NamingStyle.PascalCase };

        var code = """
            model MyModel
            protected
              constant Real badConst = 3.14;
            end MyModel;
            """;
        var violations = CheckRule(code, config);
        Assert.Single(violations);
        Assert.Contains("protected constant", violations[0].Summary);
    }

    // ── snake_case and UpperCase styles ──

    [Fact]
    public void SnakeCaseClassNaming_WhenConfigured()
    {
        var config = new NamingConventionConfig
        {
            ClassNamingRules = new Dictionary<string, NamingStyle>
            {
                ["model"] = NamingStyle.SnakeCase
            }
        };

        var code = """
            model my_snake_model
              Real x;
            end my_snake_model;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    [Fact]
    public void SnakeCaseClassNaming_PascalCaseViolation()
    {
        var config = new NamingConventionConfig
        {
            ClassNamingRules = new Dictionary<string, NamingStyle>
            {
                ["model"] = NamingStyle.SnakeCase
            }
        };

        var code = """
            model MyModel
              Real x;
            end MyModel;
            """;
        var violations = CheckRule(code, config);
        Assert.Single(violations);
        Assert.Contains("snake_case", violations[0].Summary);
    }

    // ── Exception name for class ──

    [Fact]
    public void ExceptionName_InDerClassSpecifier_NoViolation()
    {
        var config = ModelicaStandardConfig();
        config = config with { ExceptionNames = new HashSet<string> { "badDerType" } };

        var code = """
            type badDerType = der(Real, x);
            """;
        Assert.Empty(CheckRule(code, config));
    }

    // ── No type_prefix (null) ──

    [Fact]
    public void VariableWithFlowPrefix_Checked()
    {
        var code = """
            connector MyConnector
              Real p;
              flow Real M_flow;
            end MyConnector;
            """;
        // M_flow: base "M" is single char → valid
        Assert.Empty(CheckRule(code));
    }

    // ── Operator class type ──

    [Fact]
    public void OperatorClassType_Checked()
    {
        var config = new NamingConventionConfig
        {
            ClassNamingRules = new Dictionary<string, NamingStyle>
            {
                ["operator"] = NamingStyle.PascalCase
            }
        };

        var code = """
            operator record MyOperator
              Real x;
            end MyOperator;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    // ── FormatStyleName coverage ──

    [Fact]
    public void SnakeCaseVariable_WhenConfigured_NoViolation()
    {
        var config = ModelicaStandardConfig();
        config = config with { PublicVariableNaming = NamingStyle.SnakeCase };

        var code = """
            model MyModel
              Real my_variable;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    // ── AdditionalPatterns ──

    [Fact]
    public void AdditionalPattern_ClassName_MatchesPattern_NoViolation()
    {
        var config = ModelicaStandardConfig() with
        {
            AdditionalPatterns = new Dictionary<string, List<string>>
            {
                ["model"] = [@"^[A-Z][a-zA-Z]+(_\d+)+$"]
            }
        };

        var code = """
            model Version_2026_1
              Real x;
            end Version_2026_1;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    [Fact]
    public void AdditionalPattern_ClassName_WrongSlot_StillViolation()
    {
        var config = ModelicaStandardConfig() with
        {
            AdditionalPatterns = new Dictionary<string, List<string>>
            {
                ["function"] = [@"^[A-Z][a-zA-Z]+(_\d+)+$"]
            }
        };

        var code = """
            model Version_2026_1
              Real x;
            end Version_2026_1;
            """;
        var violations = CheckRule(code, config);
        Assert.Single(violations);
        Assert.Contains("'Version_2026_1'", violations[0].Summary);
    }

    [Fact]
    public void AdditionalPattern_ElementName_MatchesPattern_NoViolation()
    {
        var config = ModelicaStandardConfig() with
        {
            AdditionalPatterns = new Dictionary<string, List<string>>
            {
                ["publicVariable"] = [@"^[A-Z_]+$"]
            }
        };

        var code = """
            model MyModel
              Real MY_SPECIAL_VAR;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    [Fact]
    public void AdditionalPattern_ProtectedElement_UsesCorrectSlot()
    {
        var config = ModelicaStandardConfig() with
        {
            AdditionalPatterns = new Dictionary<string, List<string>>
            {
                ["protectedParameter"] = [@"^_[a-z]+$"]
            }
        };

        var code = """
            model MyModel
            protected
              parameter Real _internal = 1.0;
            end MyModel;
            """;
        Assert.Empty(CheckRule(code, config));
    }

    [Fact]
    public void AdditionalPattern_ViolationMessage_IncludesPatternReference()
    {
        var config = ModelicaStandardConfig() with
        {
            AdditionalPatterns = new Dictionary<string, List<string>>
            {
                ["model"] = [@"^Version_\d+$"]
            }
        };

        var code = """
            model bad_model_name
              Real x;
            end bad_model_name;
            """;
        var violations = CheckRule(code, config);
        Assert.Single(violations);
        Assert.Contains("or match an allowed pattern", violations[0].Summary);
    }

    [Fact]
    public void AdditionalPattern_NoPatterns_ViolationMessage_NoPatternReference()
    {
        var code = """
            model bad_model_name
              Real x;
            end bad_model_name;
            """;
        var violations = CheckRule(code);
        Assert.Single(violations);
        Assert.DoesNotContain("pattern", violations[0].Summary);
    }

    [Fact]
    public void AdditionalPattern_ExceptionNameTakesPrecedence()
    {
        var config = ModelicaStandardConfig() with
        {
            ExceptionNames = new HashSet<string> { "SPECIAL_NAME" },
            AdditionalPatterns = new Dictionary<string, List<string>>
            {
                ["model"] = [@"^DoesNotMatch$"]
            }
        };

        var code = """
            model SPECIAL_NAME
              Real x;
            end SPECIAL_NAME;
            """;
        Assert.Empty(CheckRule(code, config));
    }
}
