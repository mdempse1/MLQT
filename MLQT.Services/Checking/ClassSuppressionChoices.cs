using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace MLQT.Services.Checking;

/// <summary>
/// What the Code Review page's "Suppress rules in this class" dialog offers, and the
/// <c>__MLQT(suppress=…)</c> list a choice turns into.
/// </summary>
/// <remarks>
/// <para>Written for generated code such as Dymola's <c>_fmu</c> import models, whose findings are
/// almost all in the classes nested inside them. A class-level waiver reaches those classes, but the
/// findings table shows one class at a time, so the rules worth waiving on the model were never in
/// front of the user when it was selected. The choices therefore count findings over the class
/// <b>and everything nested in it</b>.</para>
///
/// <para>Here rather than in the dialog so the two decisions — which rules to offer, and what list
/// to write — can be tested without rendering anything.</para>
/// </remarks>
public static class ClassSuppressionChoices
{
    /// <summary>One rule the dialog offers.</summary>
    /// <param name="Findings">How many findings for it are in the class and the classes nested in it.</param>
    /// <param name="Suppressed">Whether the class's list already names it.</param>
    public sealed record Choice(string Id, string Title, string Category, int Findings, bool Suppressed);

    /// <summary>
    /// The rules to offer for <paramref name="classId"/>: every rule with a finding in it or below
    /// it, and every rule its list already names, or with <paramref name="everyRule"/> the whole
    /// catalogue. Never a diagnostic, which no annotation can waive. Ordered by category then title,
    /// as the settings dialog lists them.
    /// </summary>
    public static IReadOnlyList<Choice> For(
        string classId, IEnumerable<LogMessage> findings, IReadOnlyCollection<string> currentList, bool everyRule)
    {
        var counts = findings
            .Where(f => f.RuleId is { } id && !RuleIds.IsDiagnostic(id) && ModelicaName.IsInSubtree(f.ModelName, classId))
            .GroupBy(f => f.RuleId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        // Named one by one, not through "*": the wildcard is its own switch, and ticking every row
        // because of it would mean unticking one could not be written.
        bool Named(string id) => currentList.Any(entry => entry != "*" && SuppressionSet.Names(entry, id));

        var ids = new HashSet<string>(counts.Keys, StringComparer.Ordinal);
        foreach (var rule in RuleCatalog.BuiltIn.Values)
            if (!RuleIds.IsDiagnostic(rule.Id) && (everyRule || Named(rule.Id)))
                ids.Add(rule.Id);

        return ids
            .Select(id => RuleCatalog.BuiltIn.TryGetValue(id, out var rule)
                ? new Choice(id, rule.Title, rule.Category, counts.GetValueOrDefault(id), Named(id))
                // A rule the catalogue does not know, from an external tool: offered by its id.
                : new Choice(id, id, "Other", counts.GetValueOrDefault(id), Named(id)))
            .OrderBy(c => c.Category, StringComparer.Ordinal)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The list to write, given what it says now and what the user chose.
    /// </summary>
    /// <remarks>
    /// <para>An entry the user did not touch is kept as written — a short id stays short, so saving
    /// without changing anything changes nothing in the file. An entry naming a rule the dialog did
    /// not offer (a rule from another tool, or one this version does not know) is kept too: the dialog
    /// cannot see it, so it has no business deleting it.</para>
    ///
    /// <para>With <paramref name="everyRule"/> the list is <c>*</c> and the rules the user ticked
    /// besides, so turning the wildcard off again restores the choice underneath it.</para>
    /// </remarks>
    /// <param name="offered">The rules the dialog showed, whose state it decides.</param>
    /// <param name="chosen">The rules among them the user left ticked.</param>
    public static IReadOnlyList<string> ListFor(
        IReadOnlyCollection<string> currentList, IEnumerable<string> offered, IEnumerable<string> chosen, bool everyRule)
    {
        var offeredSet = offered.ToHashSet(StringComparer.Ordinal);
        var chosenSet = chosen.ToHashSet(StringComparer.Ordinal);
        var list = new List<string>();
        var covered = new HashSet<string>(StringComparer.Ordinal);

        if (everyRule)
            list.Add("*");

        foreach (var entry in currentList)
        {
            if (entry == "*")
                continue;

            var names = offeredSet.Where(id => SuppressionSet.Names(entry, id)).ToList();
            if (names.Count == 0)
            {
                list.Add(entry);   // not the dialog's to decide
                continue;
            }

            if (names.Any(chosenSet.Contains))
            {
                list.Add(entry);
                covered.UnionWith(names);
            }
        }

        foreach (var id in chosenSet.Where(offeredSet.Contains).Order(StringComparer.Ordinal))
            if (covered.Add(id))
                list.Add(id);

        return list;
    }
}
