using Xunit;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class CheckClassAnnotationsTests
{
    private List<DataTypes.LogMessage> CheckRule(
        string code,
        bool checkDocInfo = false,
        bool checkDocRevisions = false,
        bool checkIcon = false,
        string basePackage = "",
        Func<string, string, bool>? baseClassHasIcon = null)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckClassAnnotations(checkDocInfo, checkDocRevisions, checkIcon, basePackage, baseClassHasIcon);
        visitor.Visit(parseTree);
        return visitor.RuleViolations;
    }

    // ============================================================================
    // Documentation info checks
    // ============================================================================

    [Fact]
    public void DocInfo_Present_NoViolation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html><p>Info</p></html>"));
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true);

        Assert.Empty(violations);
    }

    [Fact]
    public void DocInfo_Missing_Violation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true);

        Assert.Single(violations);
        Assert.Contains("Documentation info", violations[0].Summary);
    }

    [Fact]
    public void DocInfo_Disabled_NoViolation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: false);

        Assert.Empty(violations);
    }

    // ============================================================================
    // Documentation revisions checks
    // ============================================================================

    [Fact]
    public void DocRevisions_Present_NoViolation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html>Info</html>", revisions="<html>v1</html>"));
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocRevisions: true);

        Assert.Empty(violations);
    }

    [Fact]
    public void DocRevisions_Missing_Violation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html>Info</html>"));
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocRevisions: true);

        Assert.Single(violations);
        Assert.Contains("Documentation revisions", violations[0].Summary);
    }

    [Fact]
    public void DocRevisions_NoAnnotationAtAll_Violation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocRevisions: true);

        Assert.Single(violations);
        Assert.Contains("Documentation revisions", violations[0].Summary);
    }

    // ============================================================================
    // Icon checks
    // ============================================================================

    [Fact]
    public void Icon_Present_NoViolation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}})));
            end TestModel;
            """;

        var violations = CheckRule(code, checkIcon: true);

        Assert.Empty(violations);
    }

    [Fact]
    public void Icon_Missing_Violation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var violations = CheckRule(code, checkIcon: true);

        Assert.Single(violations);
        Assert.Contains("Icon", violations[0].Summary);
    }

    [Fact]
    public void Icon_Disabled_NoViolation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var violations = CheckRule(code, checkIcon: false);

        Assert.Empty(violations);
    }

    // ============================================================================
    // Combined checks
    // ============================================================================

    [Fact]
    public void AllChecks_AllMissing_ThreeViolations()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true, checkIcon: true);

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.Summary.Contains("Documentation info"));
        Assert.Contains(violations, v => v.Summary.Contains("Documentation revisions"));
        Assert.Contains(violations, v => v.Summary.Contains("Icon"));
    }

    [Fact]
    public void AllChecks_AllPresent_NoViolations()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(
                Documentation(info="<html>Info</html>", revisions="<html>v1</html>"),
                Icon(coordinateSystem(extent={{-100,-100},{100,100}}))
              );
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true, checkIcon: true);

        Assert.Empty(violations);
    }

    [Fact]
    public void AllChecks_OnlyDocPresent_IconViolation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html>Info</html>", revisions="<html>v1</html>"));
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true, checkIcon: true);

        Assert.Single(violations);
        Assert.Contains("Icon", violations[0].Summary);
    }

    [Fact]
    public void AllChecks_OnlyIconPresent_DocViolations()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}})));
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true, checkIcon: true);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.Summary.Contains("Documentation info"));
        Assert.Contains(violations, v => v.Summary.Contains("Documentation revisions"));
    }

    // ============================================================================
    // Inherited icon via extends clause
    // ============================================================================

    [Fact]
    public void Icon_InheritedViaCallback_NoViolation()
    {
        var code = """
            model TestModel "A model"
              extends BaseModel;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        // Callback says the base class has an icon
        Func<string, string, bool> callback = (baseClassName, currentModel) => baseClassName == "BaseModel";

        var violations = CheckRule(code, checkIcon: true, baseClassHasIcon: callback);

        Assert.Empty(violations);
    }

    [Fact]
    public void Icon_InheritedCallbackReturnsFalse_Violation()
    {
        var code = """
            model TestModel "A model"
              extends BaseModel;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        // Callback says no base class has an icon
        Func<string, string, bool> callback = (baseClassName, currentModel) => false;

        var violations = CheckRule(code, checkIcon: true, baseClassHasIcon: callback);

        Assert.Single(violations);
        Assert.Contains("Icon", violations[0].Summary);
    }

    [Fact]
    public void Icon_NoCallback_NoInheritanceCheck_Violation()
    {
        var code = """
            model TestModel "A model"
              extends BaseModel;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        // No callback provided — inheritance not checked
        var violations = CheckRule(code, checkIcon: true, baseClassHasIcon: null);

        Assert.Single(violations);
        Assert.Contains("Icon", violations[0].Summary);
    }

    [Fact]
    public void Icon_MultipleExtends_FirstHasIcon_NoViolation()
    {
        var code = """
            model TestModel "A model"
              extends BaseA;
              extends BaseB;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        Func<string, string, bool> callback = (baseClassName, currentModel) => baseClassName == "BaseA";

        var violations = CheckRule(code, checkIcon: true, baseClassHasIcon: callback);

        Assert.Empty(violations);
    }

    [Fact]
    public void Icon_MultipleExtends_SecondHasIcon_NoViolation()
    {
        var code = """
            model TestModel "A model"
              extends BaseA;
              extends BaseB;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        Func<string, string, bool> callback = (baseClassName, currentModel) => baseClassName == "BaseB";

        var violations = CheckRule(code, checkIcon: true, baseClassHasIcon: callback);

        Assert.Empty(violations);
    }

    [Fact]
    public void Icon_MultipleExtends_NoneHasIcon_Violation()
    {
        var code = """
            model TestModel "A model"
              extends BaseA;
              extends BaseB;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        Func<string, string, bool> callback = (baseClassName, currentModel) => false;

        var violations = CheckRule(code, checkIcon: true, baseClassHasIcon: callback);

        Assert.Single(violations);
        Assert.Contains("Icon", violations[0].Summary);
    }

    [Fact]
    public void Icon_DirectIconPresent_CallbackNotInvoked()
    {
        var code = """
            model TestModel "A model"
              extends BaseModel;
              Real x;
            equation
              x = 1.0;
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}})));
            end TestModel;
            """;

        var callbackInvoked = false;
        Func<string, string, bool> callback = (baseClassName, currentModel) =>
        {
            callbackInvoked = true;
            return true;
        };

        var violations = CheckRule(code, checkIcon: true, baseClassHasIcon: callback);

        Assert.Empty(violations);
        Assert.False(callbackInvoked);
    }

    // ============================================================================
    // Callback receives correct arguments
    // ============================================================================

    [Fact]
    public void Icon_CallbackReceivesCorrectBaseClassName()
    {
        var code = """
            model TestModel "A model"
              extends Modelica.Icons.Package;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        string? receivedBaseClass = null;
        Func<string, string, bool> callback = (baseClassName, currentModel) =>
        {
            receivedBaseClass = baseClassName;
            return true;
        };

        CheckRule(code, checkIcon: true, baseClassHasIcon: callback);

        Assert.Equal("Modelica.Icons.Package", receivedBaseClass);
    }

    [Fact]
    public void Icon_CallbackReceivesCorrectCurrentModelName()
    {
        var code = """
            within MyLib;
            model TestModel "A model"
              extends BaseModel;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        string? receivedModelName = null;
        Func<string, string, bool> callback = (baseClassName, currentModel) =>
        {
            receivedModelName = currentModel;
            return true;
        };

        CheckRule(code, checkIcon: true, baseClassHasIcon: callback);

        Assert.Equal("MyLib.TestModel", receivedModelName);
    }

    [Fact]
    public void Icon_CallbackReceivesCorrectCurrentModelName_WithBasePackage()
    {
        var code = """
            model TestModel "A model"
              extends BaseModel;
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        string? receivedModelName = null;
        Func<string, string, bool> callback = (baseClassName, currentModel) =>
        {
            receivedModelName = currentModel;
            return true;
        };

        CheckRule(code, checkIcon: true, basePackage: "SomePackage", baseClassHasIcon: callback);

        Assert.Equal("SomePackage.TestModel", receivedModelName);
    }

    // ============================================================================
    // Annotation edge cases
    // ============================================================================

    [Fact]
    public void AnnotationWithNoDocumentation_OnlyNonDocArgs()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(version="1.0", dateModified="2024-01-01");
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true);

        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void AnnotationWithEmptyDocumentation()
    {
        // Documentation() with no sub-arguments — info and revisions are still missing
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation());
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true);

        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void AnnotationWithDocInfoOnly_RevisionsViolation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(info="<html>Info</html>"));
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true);

        Assert.Single(violations);
        Assert.Contains("Documentation revisions", violations[0].Summary);
    }

    [Fact]
    public void AnnotationWithDocRevisionsOnly_InfoViolation()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
              annotation(Documentation(revisions="<html>v1</html>"));
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true);

        Assert.Single(violations);
        Assert.Contains("Documentation info", violations[0].Summary);
    }

    // ============================================================================
    // Nested classes are skipped
    // ============================================================================

    [Fact]
    public void NestedClass_SkippedByParentVisitor()
    {
        // Only the outermost class is checked; nested ones are checked independently
        var code = """
            package TestPackage "A package"
              model InnerModel "An inner model"
                Real x;
              equation
                x = 1.0;
              end InnerModel;
              annotation(
                Documentation(info="<html>Info</html>", revisions="<html>v1</html>"),
                Icon(coordinateSystem(extent={{-100,-100},{100,100}}))
              );
            end TestPackage;
            """;

        var violations = CheckRule(code, checkDocInfo: true, checkDocRevisions: true, checkIcon: true);

        // Only TestPackage is checked (and has all annotations), InnerModel is skipped
        Assert.Empty(violations);
    }

    // ============================================================================
    // External annotation section
    // ============================================================================

    [Fact]
    public void ExternalAnnotation_IconDetected()
    {
        // Models with external function declarations have an annotation at the external level
        var code = """
            function TestFunc "A function"
              input Real x;
              output Real y;
            external "C" y = testFunc(x)
              annotation(Icon(coordinateSystem(extent={{-100,-100},{100,100}})));
            end TestFunc;
            """;

        var violations = CheckRule(code, checkIcon: true);

        Assert.Empty(violations);
    }

    // ============================================================================
    // BasePackage integration
    // ============================================================================

    [Fact]
    public void WithBasePackage_ViolationModelNameIncludesPackage()
    {
        var code = """
            model TestModel "A model"
              Real x;
            equation
              x = 1.0;
            end TestModel;
            """;

        var violations = CheckRule(code, checkDocInfo: true, basePackage: "MyLib");

        Assert.Single(violations);
        Assert.Contains("MyLib.TestModel", violations[0].ModelName);
    }
}
