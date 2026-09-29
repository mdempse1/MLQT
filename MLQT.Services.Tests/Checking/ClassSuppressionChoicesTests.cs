using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// What the "Suppress rules in this class" dialog offers, and the list a choice writes.
/// </summary>
/// <remarks>
/// The case it is for: a Dymola <c>_fmu</c> import model whose findings are nearly all in the
/// classes nested in it. Selecting the model showed none of them, so the rules worth waiving on it
/// were never offered.
/// </remarks>
public class ClassSuppressionChoicesTests
{
    private static LogMessage Finding(string model, string? rule) =>
        new(model, "warning", 1, "m") { RuleId = rule };

    private static readonly LogMessage[] Findings =
    [
        Finding("Lib.Fmu", RuleIds.ClassDescription),
        Finding("Lib.Fmu.Types.T", RuleIds.ClassDescription),
        Finding("Lib.Fmu.Types.T", RuleIds.ParameterDescription),
        Finding("Lib.Fmu.Types.U", RuleIds.ParameterDescription),
        Finding("Lib.Fmu.Types.U", RuleIds.ParameterDescription),
        Finding("Lib.Other", RuleIds.ImportStatementsFirst),
        Finding("Lib.FmuX", RuleIds.ImportStatementsFirst),
        Finding("Lib.Fmu", RuleIds.CheckFailed),
        Finding("Lib.Fmu", null),
    ];

    [Fact]
    public void TheRulesOffered_AreThoseWithFindingsInTheClassAndBelow_Counted()
    {
        var choices = ClassSuppressionChoices.For("Lib.Fmu", Findings, [], everyRule: false);

        Assert.Equal(
            [(RuleIds.ClassDescription, 2), (RuleIds.ParameterDescription, 3)],
            choices.Select(c => (c.Id, c.Findings)).OrderBy(c => c.Id, StringComparer.Ordinal));
        Assert.All(choices, c => Assert.False(c.Suppressed));
    }

    [Fact]
    public void ADiagnosticIsNeverOffered_EvenWithEveryRule()
    {
        var choices = ClassSuppressionChoices.For("Lib.Fmu", Findings, [], everyRule: true);

        Assert.DoesNotContain(choices, c => RuleIds.IsDiagnostic(c.Id));
        Assert.Contains(choices, c => c.Id == RuleIds.ImportStatementsFirst && c.Findings == 0);
    }

    [Fact]
    public void ARuleTheListAlreadyNames_IsOfferedTicked_EvenWithNoFindingLeft()
    {
        // Its findings are gone because it is suppressed; it still has to be there to untick.
        var choices = ClassSuppressionChoices.For("Lib.Fmu", [], ["Style.ImportStatementsFirst"], everyRule: false);

        var choice = Assert.Single(choices);
        Assert.Equal(RuleIds.ImportStatementsFirst, choice.Id);
        Assert.True(choice.Suppressed);
        Assert.Equal(0, choice.Findings);
    }

    [Fact]
    public void TheWildcard_DoesNotTickEveryRow()
    {
        var choices = ClassSuppressionChoices.For("Lib.Fmu", Findings, ["*"], everyRule: false);

        Assert.All(choices, c => Assert.False(c.Suppressed));
    }

    [Fact]
    public void ARuleOutsideTheCatalogue_IsOfferedByItsId()
    {
        var choices = ClassSuppressionChoices.For("Lib.Fmu", [Finding("Lib.Fmu", "Vendor.Rule")], [], everyRule: false);

        var choice = Assert.Single(choices);
        Assert.Equal(("Vendor.Rule", "Vendor.Rule"), (choice.Id, choice.Title));
    }

    [Fact]
    public void SavingWithoutAChange_WritesTheListAsItWas()
    {
        string[] current = ["Doc.ClassDescription", "Vendor.Rule"];
        string[] offered = [RuleIds.ClassDescription, RuleIds.ParameterDescription];

        var list = ClassSuppressionChoices.ListFor(current, offered, [RuleIds.ClassDescription], everyRule: false);

        Assert.Equal(current, list);
    }

    [Fact]
    public void UntickingARule_TakesItsEntryOut_AndTickingOne_AddsItsFullId()
    {
        string[] offered = [RuleIds.ClassDescription, RuleIds.ParameterDescription];

        var list = ClassSuppressionChoices.ListFor(
            ["Doc.ClassDescription"], offered, [RuleIds.ParameterDescription], everyRule: false);

        Assert.Equal([RuleIds.ParameterDescription], list);
    }

    [Fact]
    public void AnEntryTheDialogDidNotOffer_IsKept()
    {
        var list = ClassSuppressionChoices.ListFor(["Vendor.Rule"], [RuleIds.ClassDescription], [], everyRule: false);

        Assert.Equal(["Vendor.Rule"], list);
    }

    [Fact]
    public void TheWildcard_IsWrittenFirst_WithTheTickedRulesKeptUnderneath()
    {
        string[] offered = [RuleIds.ClassDescription];

        Assert.Equal(["*", RuleIds.ClassDescription],
            ClassSuppressionChoices.ListFor([], offered, [RuleIds.ClassDescription], everyRule: true));
        Assert.Empty(ClassSuppressionChoices.ListFor(["*"], offered, [], everyRule: false));
    }

    [Fact]
    public void ARuleAlreadyNamed_IsNotAddedAgain()
    {
        string[] offered = [RuleIds.ClassDescription];

        var list = ClassSuppressionChoices.ListFor(
            ["Doc.ClassDescription", RuleIds.ClassDescription], offered, [RuleIds.ClassDescription], everyRule: false);

        Assert.Equal(["Doc.ClassDescription", RuleIds.ClassDescription], list);
    }
}
