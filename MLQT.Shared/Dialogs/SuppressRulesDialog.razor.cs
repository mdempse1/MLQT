using MLQT.Services.Checking;
using ModelicaParser.DataTypes;

namespace MLQT.Shared.Dialogs;

/// <summary>
/// Turns rules on and off in a class's <c>__MLQT(suppress=…)</c> list, which reaches the class and
/// every class nested in it.
///
/// <para>One dialog rather than a button per rule: there are more than forty rules, and the ones that
/// matter for a class are the ones with findings in it and below it — which is what it lists, with
/// counts, so a Dymola <c>_fmu</c> model's nested-class findings are in front of the user when the
/// model is selected. <see cref="ClassSuppressionChoices"/> decides what is offered and what list a
/// choice writes; this only holds the state.</para>
/// </summary>
public partial class SuppressRulesDialog
{
    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    /// <summary>The class's full name.</summary>
    [Parameter] public string ClassId { get; set; } = "";

    /// <summary>What the title calls it.</summary>
    [Parameter] public string ClassName { get; set; } = "";

    /// <summary>The findings to count, over the whole project; the dialog narrows them to the class.</summary>
    [Parameter] public IReadOnlyList<LogMessage> Findings { get; set; } = [];

    /// <summary>The class's <c>suppress</c> list as written now.</summary>
    [Parameter] public IReadOnlyCollection<string> CurrentList { get; set; } = [];

    /// <summary>The class's <c>reason</c> as written now.</summary>
    [Parameter] public string? CurrentReason { get; set; }

    /// <summary>What enclosing packages already suppress here, one line each.</summary>
    [Parameter] public IReadOnlyList<string> EnclosingNotes { get; set; } = [];

    /// <summary>What the dialog returns: the list to write and the reason to write with it.</summary>
    public sealed record Choice(IReadOnlyList<string> List, string? Reason);

    private IReadOnlyList<ClassSuppressionChoices.Choice> _choices = [];
    private readonly HashSet<string> _ticked = new(StringComparer.Ordinal);
    private bool _everyRule;
    private bool _showAllRules;
    private string? _reason;

    protected override void OnParametersSet()
    {
        _everyRule = CurrentList.Contains("*");
        _reason = CurrentReason;
        _ticked.Clear();
        foreach (var choice in ClassSuppressionChoices.For(ClassId, Findings, CurrentList, everyRule: true))
            if (choice.Suppressed)
                _ticked.Add(choice.Id);
        Refresh();
    }

    private void Refresh() =>
        _choices = ClassSuppressionChoices.For(ClassId, Findings, CurrentList, _showAllRules);

    private void SetShowAllRules(bool on)
    {
        _showAllRules = on;
        Refresh();
    }

    private void SetEveryRule(bool on) => _everyRule = on;

    private void SetTicked(string id, bool on)
    {
        if (on) _ticked.Add(id);
        else _ticked.Remove(id);
    }

    /// <summary>The list the current state writes.</summary>
    /// <remarks>
    /// Decided over every rule the dialog could show, not only the rows on screen now: a rule ticked
    /// with every rule shown, then hidden again by turning that off, is still ticked.
    /// </remarks>
    internal IReadOnlyList<string> ListToWrite() =>
        ClassSuppressionChoices.ListFor(
            CurrentList,
            ClassSuppressionChoices.For(ClassId, Findings, CurrentList, everyRule: true).Select(c => c.Id),
            _ticked,
            _everyRule);

    internal static string FindingsLabel(int count) => count == 1 ? "1 finding" : $"{count:N0} findings";

    private void Cancel() => MudDialog?.Cancel();

    private void Save() =>
        MudDialog?.Close(DialogResult.Ok(new Choice(ListToWrite(), string.IsNullOrWhiteSpace(_reason) ? null : _reason.Trim())));
}
