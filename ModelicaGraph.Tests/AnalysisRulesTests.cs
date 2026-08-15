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
}
