using Bunit;
using MLQT.Shared.Dialogs;
using MLQT.Shared.Pages;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MudBlazor;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// The Code Review dialog that sets a class's <c>__MLQT(suppress=…)</c> list, which reaches the class
/// and every class nested in it. What it offers and what list it writes are
/// <c>ClassSuppressionChoices</c>'s, tested there; this is what only a rendered dialog shows — that
/// the rows are the class's, and what it closes with.
/// </summary>
public class SuppressRulesDialogTests : MlqtComponentTestBase
{
    private static LogMessage Finding(string model, string rule) =>
        new(model, "warning", 1, "m") { RuleId = rule };

    /// <summary>A Dymola import model whose findings are in its nested classes, as the real ones are.</summary>
    private static readonly LogMessage[] Findings =
    [
        Finding("Lib.Fmu.Types.T", RuleIds.ClassDescription),
        Finding("Lib.Fmu.Types.U", RuleIds.ClassDescription),
        Finding("Lib.Fmu.Records.R", RuleIds.ParameterDescription),
        Finding("Lib.Other", RuleIds.ImportStatementsFirst),
    ];

    private Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> Open(
        string[] currentList, string? reason = null, string[]? notes = null) =>
        ShowDialogAsync<SuppressRulesDialog>(new DialogParameters
        {
            { nameof(SuppressRulesDialog.ClassId), "Lib.Fmu" },
            { nameof(SuppressRulesDialog.ClassName), "Fmu" },
            { nameof(SuppressRulesDialog.Findings), Findings },
            { nameof(SuppressRulesDialog.CurrentList), currentList },
            { nameof(SuppressRulesDialog.CurrentReason), reason },
            { nameof(SuppressRulesDialog.EnclosingNotes), notes ?? [] },
        });

    private static IReadOnlyList<string> RuleRows(IRenderedComponent<MudDialogProvider> provider) =>
        provider.FindAll(".mlqt-suppress-rule").Select(r => r.TextContent).ToList();

    private static Task Tick(IRenderedComponent<MudDialogProvider> provider, string ruleId, bool on) =>
        provider.InvokeAsync(() => provider.FindAll(".mlqt-suppress-rule")
            .First(r => r.TextContent.Contains(ruleId, StringComparison.Ordinal))
            .QuerySelector("input")!
            .Change(on));

    private static Task Click(IRenderedComponent<MudDialogProvider> provider, string label) =>
        provider.InvokeAsync(() => provider.FindAll("button")
            .First(b => b.TextContent.Contains(label, StringComparison.OrdinalIgnoreCase))
            .Click());

    [Fact]
    public async Task ItListsTheRulesFoundInTheClassesNestedInIt_WithCounts()
    {
        var (provider, _) = await Open([]);

        var rows = RuleRows(provider);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Contains(RuleIds.ClassDescription) && r.Contains("2 findings"));
        Assert.Contains(rows, r => r.Contains(RuleIds.ParameterDescription) && r.Contains("1 finding"));
        Assert.DoesNotContain(rows, r => r.Contains(RuleIds.ImportStatementsFirst));
    }

    [Fact]
    public async Task ShowingEveryRule_OffersTheWholeCatalogue()
    {
        var (provider, _) = await Open([]);

        await provider.InvokeAsync(() => provider.Find(".mlqt-suppress-show-all input").Change(true));

        Assert.Contains(RuleRows(provider), r => r.Contains(RuleIds.ImportStatementsFirst));
    }

    [Fact]
    public async Task Saving_ClosesWithTheListTheTicksMake_AndTheReason()
    {
        var (provider, dialog) = await Open(["Doc.ClassDescription"], reason: "generated");

        await Tick(provider, RuleIds.ClassDescription, false);
        await Tick(provider, RuleIds.ParameterDescription, true);
        await Click(provider, "Save");

        var result = await dialog.Result;
        Assert.False(result!.Canceled);
        var choice = Assert.IsType<SuppressRulesDialog.Choice>(result.Data);
        Assert.Equal([RuleIds.ParameterDescription], choice.List);
        Assert.Equal("generated", choice.Reason);
    }

    [Fact]
    public async Task TheWildcardSwitch_WritesStar()
    {
        var (provider, dialog) = await Open([]);

        await provider.InvokeAsync(() => provider.Find(".mlqt-suppress-all input").Change(true));
        await Click(provider, "Save");

        var choice = Assert.IsType<SuppressRulesDialog.Choice>((await dialog.Result)!.Data);
        Assert.Equal(["*"], choice.List);
    }

    [Fact]
    public async Task Cancelling_ClosesCancelled()
    {
        var (provider, dialog) = await Open([]);

        await Click(provider, "Cancel");

        Assert.True((await dialog.Result)!.Canceled);
    }

    [Fact]
    public async Task AClassWithNoFindingsBelowIt_SaysSo_AndWhatAnEnclosingPackageAlreadyWaives()
    {
        var (provider, _) = await ShowDialogAsync<SuppressRulesDialog>(new DialogParameters
        {
            { nameof(SuppressRulesDialog.ClassId), "Lib.Quiet" },
            { nameof(SuppressRulesDialog.ClassName), "Quiet" },
            { nameof(SuppressRulesDialog.Findings), Findings },
            { nameof(SuppressRulesDialog.EnclosingNotes), new[] { "Lib already suppresses here: *" } },
        });

        Assert.Contains("No findings", provider.Find(".mlqt-suppress-none").TextContent);
        Assert.Contains("Lib already suppresses here: *", provider.Find(".mlqt-suppress-enclosing").TextContent);
    }

    // ---- the page's side of it -----------------------------------------------------------------

    [Theory]
    [InlineData(new string[0], "Suppress rules in this class and the classes nested in it")]
    [InlineData(new[] { "*" }, "Every rule is suppressed in this class and the classes nested in it - change")]
    [InlineData(new[] { "A", "B" }, "2 rule(s) suppressed in this class and the classes nested in it - change")]
    public void TheButtonSaysWhatTheClassSuppresses(string[] list, string expected)
    {
        Assert.Equal(expected, CodeReview.SuppressRulesTooltip(list));
    }

    [Theory]
    [InlineData(new string[0], "No rules are suppressed in this class now.")]
    [InlineData(new[] { "*", "A" }, "Every rule is suppressed in this class and the classes nested in it.")]
    [InlineData(new[] { "A" }, "1 rule(s) suppressed in this class and the classes nested in it.")]
    public void SavingSaysWhatItDid(string[] list, string expected)
    {
        Assert.Equal(expected, CodeReview.SuppressionListChanged(list));
    }

    [Fact]
    public void TheEnclosingNotes_NameEachPackageAboveWhoseListReachesTheClass()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib\n  annotation(__MLQT(suppress=\"*\"));\nend Lib;"));
        graph.AddNode(new ModelNode("Lib.Mid", "Mid", "package Mid\nend Mid;"));
        graph.AddNode(new ModelNode("Lib.Mid.Pkg", "Pkg",
            "package Pkg\n  annotation(__MLQT(suppress=\"Doc.ClassDescription\", format=false));\nend Pkg;"));

        Assert.Equal(
            ["Lib.Mid.Pkg already suppresses here: Doc.ClassDescription", "Lib already suppresses here: *"],
            CodeReview.EnclosingSuppressionNotes(graph, "Lib.Mid.Pkg.Fmu"));
    }

    [Theory]
    [InlineData(1, "1 finding")]
    [InlineData(2, "2 findings")]
    public void TheCountReadsAsEnglish(int count, string expected)
    {
        Assert.Equal(expected, SuppressRulesDialog.FindingsLabel(count));
    }
}
