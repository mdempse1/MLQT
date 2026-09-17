using MLQT.Shared.Pages;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// How the Metrics tab decides what it is looking at: which classes a scope covers, which packages
/// the comparison table lists, and which findings count as debt.
/// </summary>
public class MetricsDashboardScopeTests
{
    // ---- InScope ------------------------------------------------------------------------------

    [Fact]
    public void InScope_WithNoScope_CoversEverything()
    {
        Assert.True(MetricsDashboard.InScope("Modelica.Blocks.Sources.Sine", ""));
    }

    [Fact]
    public void InScope_CoversTheScopedClassItself()
    {
        Assert.True(MetricsDashboard.InScope("Modelica.Blocks", "Modelica.Blocks"));
    }

    [Fact]
    public void InScope_CoversClassesBeneathTheScope()
    {
        Assert.True(MetricsDashboard.InScope("Modelica.Blocks.Sources.Sine", "Modelica.Blocks"));
    }

    [Fact]
    public void InScope_DoesNotCoverASiblingWithTheScopeAsAPrefix()
    {
        // The dot is the whole point. A bare StartsWith would pull Modelica.BlocksExtra into a
        // Modelica.Blocks scope and quietly inflate every number on the page.
        Assert.False(MetricsDashboard.InScope("Modelica.BlocksExtra.Thing", "Modelica.Blocks"));
    }

    [Fact]
    public void InScope_DoesNotCoverAnUnrelatedLibrary()
    {
        Assert.False(MetricsDashboard.InScope("Buildings.Fluid.Sensors", "Modelica"));
    }

    // ---- SubPackagesOf ------------------------------------------------------------------------

    [Fact]
    public void SubPackagesOf_ListsOneLevelBelowTheScope()
    {
        var packages = new[]
        {
            "Modelica", "Modelica.Blocks", "Modelica.Blocks.Sources", "Modelica.Blocks.Math",
            "Modelica.Blocks.Sources.Nested", "Modelica.Fluid",
        };

        var children = MetricsDashboard.SubPackagesOf(packages, "Modelica.Blocks");

        Assert.Equal(new[] { "Modelica.Blocks.Math", "Modelica.Blocks.Sources" }, children);
    }

    [Fact]
    public void SubPackagesOf_WithOneRootAndNoScope_ListsThatRootsChildren()
    {
        var packages = new[] { "Modelica", "Modelica.Blocks", "Modelica.Fluid", "Modelica.Blocks.Sources" };

        var children = MetricsDashboard.SubPackagesOf(packages, scope: "");

        Assert.Equal(new[] { "Modelica.Blocks", "Modelica.Fluid" }, children);
    }

    [Fact]
    public void SubPackagesOf_WithSeveralRootsAndNoScope_ListsTheRoots()
    {
        // A project holding two libraries compares the libraries, not the insides of one of them.
        var packages = new[] { "Modelica", "Modelica.Blocks", "Buildings", "Buildings.Fluid" };

        var children = MetricsDashboard.SubPackagesOf(packages, scope: null);

        Assert.Equal(new[] { "Buildings", "Modelica" }, children);
    }

    [Fact]
    public void SubPackagesOf_ExcludesGrandchildren()
    {
        var packages = new[] { "Lib", "Lib.A", "Lib.A.Deep", "Lib.A.Deep.Deeper" };

        var children = MetricsDashboard.SubPackagesOf(packages, "Lib.A");

        Assert.Equal(new[] { "Lib.A.Deep" }, children);
    }

    [Fact]
    public void SubPackagesOf_IsOrdered()
    {
        var packages = new[] { "Lib", "Lib.Z", "Lib.A", "Lib.M" };

        Assert.Equal(new[] { "Lib.A", "Lib.M", "Lib.Z" }, MetricsDashboard.SubPackagesOf(packages, "Lib"));
    }

    [Fact]
    public void SubPackagesOf_WithNothingBelowTheScope_IsEmpty()
    {
        Assert.Empty(MetricsDashboard.SubPackagesOf(new[] { "Lib", "Lib.A" }, "Lib.A"));
    }

    // ---- IsStyleDebt --------------------------------------------------------------------------

    private static LogMessage Message(string source, string? ruleId) =>
        new("Lib.Thing", "Warning", 1, "a finding") { Source = source, RuleId = ruleId };

    [Fact]
    public void IsStyleDebt_ForAStyleFinding_Counts()
    {
        Assert.True(MetricsDashboard.IsStyleDebt(
            Message(LogMessage.StyleCheckingSource, RuleIds.OneOfEachSection)));
    }

    [Fact]
    public void IsStyleDebt_ForAParseDiagnostic_DoesNotCount()
    {
        // A diagnostic says the results you are reading are incomplete, not that the code has a
        // fixable problem. Counting one as debt puts a number on the wrong thing, and it is a number
        // no amount of fixing brings down.
        Assert.False(MetricsDashboard.IsStyleDebt(
            Message(LogMessage.StyleCheckingSource, RuleIds.SyntaxError)));
        Assert.False(MetricsDashboard.IsStyleDebt(
            Message(LogMessage.StyleCheckingSource, RuleIds.CheckFailed)));
    }

    [Fact]
    public void IsStyleDebt_ForAnExternalToolMessage_DoesNotCount()
    {
        Assert.False(MetricsDashboard.IsStyleDebt(
            Message(LogMessage.ExternalToolSource, RuleIds.OneOfEachSection)));
    }

    [Fact]
    public void IsStyleDebt_ForAStyleFindingWithNoRuleId_Counts()
    {
        // Not a diagnostic, so it is debt. Guards the null path through IsDiagnostic.
        Assert.True(MetricsDashboard.IsStyleDebt(Message(LogMessage.StyleCheckingSource, null)));
    }
}
