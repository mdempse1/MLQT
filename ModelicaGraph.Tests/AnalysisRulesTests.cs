using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>Phase 6: Wave-1 analyses flowing through the shared style-check pipeline.</summary>
public class AnalysisRulesTests
{
    private static List<Finding> Check(string code, StyleCheckingSettings settings)
        => StyleChecking.RunStyleCheckingFindings(new ModelDefinition("M", code), settings, "TestModel");

    [Fact]
    public void DuplicateDeclaration_FlowsThrough_WithErrorSeverity()
    {
        var settings = new StyleCheckingSettings { CheckDuplicateDeclarations = true };
        var code = "model TestModel\n  Real a;\n  Real a;\nend TestModel;";
        var f = Assert.Single(Check(code, settings), x => x.RuleId == RuleIds.DuplicateDeclaration);
        Assert.Equal(RuleSeverity.Error, f.Severity);   // catalog default for this rule
        Assert.Equal("a", f.ElementPath);
    }

    [Fact]
    public void DuplicateDeclaration_HonoursMlqtSuppression()
    {
        var settings = new StyleCheckingSettings { CheckDuplicateDeclarations = true };
        var code = "model TestModel\n  Real a annotation(__MLQT(suppress=\"MLQT.Duplicate.Declaration\"));\n  Real a;\nend TestModel;";
        Assert.DoesNotContain(Check(code, settings), x => x.RuleId == RuleIds.DuplicateDeclaration);
    }

    [Fact]
    public void NoDuplicate_NoFinding()
    {
        var settings = new StyleCheckingSettings { CheckDuplicateDeclarations = true };
        var code = "model TestModel\n  Real a;\n  Real b;\nend TestModel;";
        Assert.DoesNotContain(Check(code, settings), x => x.RuleId == RuleIds.DuplicateDeclaration);
    }

    [Fact]
    public void DisabledByDefault_NoFindingWithoutOptIn()
    {
        var code = "model TestModel\n  Real a;\n  Real a;\nend TestModel;";
        Assert.Empty(Check(code, new StyleCheckingSettings()));   // no rules enabled → pipeline skipped
    }

    [Fact]
    public void MissingUnit_FlowsThrough_WithWarningSeverity()
    {
        var settings = new StyleCheckingSettings { CheckMissingUnits = true };
        var f = Assert.Single(Check("model TestModel\n  Real x;\nend TestModel;", settings),
            x => x.RuleId == RuleIds.MissingUnit);
        Assert.Equal(RuleSeverity.Warning, f.Severity);
        Assert.Equal("x", f.ElementPath);
    }

    [Fact]
    public void MissingUnit_HonoursMlqtSuppression()
    {
        var settings = new StyleCheckingSettings { CheckMissingUnits = true };
        var code = "model TestModel\n  Real x annotation(__MLQT(suppress=\"MLQT.Units.MissingUnit\"));\nend TestModel;";
        Assert.DoesNotContain(Check(code, settings), x => x.RuleId == RuleIds.MissingUnit);
    }
}
