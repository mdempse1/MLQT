using ModelicaParser.DataTypes;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// What suppressing a rule from the Code Review page covers (backlog B119).
/// </summary>
/// <remarks>
/// The asymmetry that makes this worth testing: clearing too few findings leaves stale rows a
/// re-check would tidy, and clearing too many hides findings nobody waived — which surface again only
/// after the next full check, long after anyone would connect the two.
/// </remarks>
public class SuppressionScopeTests
{
    private static LogMessage Finding(string model, string rule, string? element = null) =>
        new(model, "warning", 1, $"{rule} on {model}") { RuleId = rule, ElementPath = element };

    [Theory]
    [InlineData("gain", "gain")]              // a simple component name scopes the waiver
    [InlineData("offset", "offset")]
    [InlineData(null, null)]                  // a class-level finding waives the class
    [InlineData("", null)]
    [InlineData("port.medium", null)]         // dotted: inside a component, so there is no declaration
    [InlineData("Base.x", null)]              // inherited, likewise
    public void OnlyASimpleElementNameScopesTheWaiver(string? elementPath, string? expected)
    {
        Assert.Equal(expected, SuppressionScope.ComponentFor(elementPath));
    }

    [Fact]
    public void AClassLevelWaiverClearsEveryFindingForThatRuleOnThatClass()
    {
        var waived = SuppressionScope.WaivedBy("Lib.Model", "MLQT.Units.MissingUnit", component: null);

        Assert.True(waived(Finding("Lib.Model", "MLQT.Units.MissingUnit")));
        Assert.True(waived(Finding("Lib.Model", "MLQT.Units.MissingUnit", "gain")));
        Assert.True(waived(Finding("Lib.Model", "MLQT.Units.MissingUnit", "offset")));
    }

    [Fact]
    public void AComponentWaiverLeavesTheSameRuleOnOtherComponents()
    {
        // The one that matters: suppressing a rule on `gain` must not silence it on `offset` two
        // lines below, which the user can still see and has not agreed to.
        var waived = SuppressionScope.WaivedBy("Lib.Model", "MLQT.Units.MissingUnit", component: "gain");

        Assert.True(waived(Finding("Lib.Model", "MLQT.Units.MissingUnit", "gain")));
        Assert.False(waived(Finding("Lib.Model", "MLQT.Units.MissingUnit", "offset")));
        Assert.False(waived(Finding("Lib.Model", "MLQT.Units.MissingUnit")));
    }

    [Fact]
    public void ItNeverReachesAnotherRule()
    {
        var waived = SuppressionScope.WaivedBy("Lib.Model", "MLQT.Units.MissingUnit", component: null);

        Assert.False(waived(Finding("Lib.Model", "MLQT.Doc.ClassDescription")));
        Assert.False(waived(Finding("Lib.Model", "MLQT.Doc.ClassDescription", "gain")));
    }

    [Fact]
    public void ItNeverReachesAnotherClass()
    {
        // A waiver is written into one class's source and says nothing about any other — including a
        // class of the same leaf name in another library, which is why the match is on the full id.
        var waived = SuppressionScope.WaivedBy("Lib.Model", "R", component: null);

        Assert.False(waived(Finding("Lib.Other", "R")));
        Assert.False(waived(Finding("OtherLib.Model", "R")));
        Assert.False(waived(Finding("Lib.Model.Nested", "R")));
    }

    [Fact]
    public void MatchingIsCaseSensitive_BecauseModelicaIs()
    {
        var waived = SuppressionScope.WaivedBy("Lib.Model", "R", component: "gain");

        Assert.False(waived(Finding("lib.model", "R", "gain")));
        Assert.False(waived(Finding("Lib.Model", "R", "Gain")));
    }

    [Theory]
    [InlineData(null, "Suppressed rule 'MLQT.Units.MissingUnit'.")]
    [InlineData("gain", "Suppressed rule 'MLQT.Units.MissingUnit' on 'gain'.")]
    public void TheMessageSaysHowFarItWent(string? component, string expected)
    {
        // The user's only feedback on scope. A component waiver that announced itself as a class
        // waiver would read as having done more than it did.
        Assert.Equal(expected, SuppressionScope.Describe("MLQT.Units.MissingUnit", component));
    }
}
