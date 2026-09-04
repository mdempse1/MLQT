using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// "Would these settings report something different?" — the question the settings dialog asks to
/// decide whether Apply has to re-check, and the one that decides whether a user's edit appears to do
/// anything.
///
/// <para>It was written out in the dialog and compared five things, missing
/// <see cref="StyleCheckingSettings.ExcludedLibraries"/>, which the same dialog edits: adding a
/// library to the excluded list saved the setting, raised no re-check, and left its findings on the
/// Code Review list until the project was reloaded. The phase 6 note named that risk for rules and
/// solved it for rules by making the list data-driven; this is the guard for the fields that are not
/// rules (backlog B90).</para>
/// </summary>
public class StyleCheckingSettingsComparisonTests
{
    /// <summary>
    /// Persisted properties that deliberately do not trigger a re-check, and why. An entry here is a
    /// decision; a property in neither list fails the test below.
    /// </summary>
    private static readonly Dictionary<string, string> NotAboutFindings = new(StringComparer.Ordinal)
    {
        ["CommitRequiresIssueNumber"] = "Commit-message policy. No rule reads it.",
        ["IssueNumberAtEnd"] = "Commit-message policy. No rule reads it.",
        ["SvnBranchDirectories"] = "Which directories SVN branch discovery looks in. No rule reads it.",
    };

    /// <summary>Every property that is written to <c>.mlqt/settings.json</c>.</summary>
    private static IEnumerable<PropertyInfo> PersistedProperties() =>
        typeof(StyleCheckingSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            // A computed facade over the severity map is not a field of its own; the map covers it.
            .Where(p => p.CanWrite || IsCollection(p.PropertyType));

    private static bool IsCollection(Type t) =>
        t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t);

    [Fact]
    public void EveryPersistedPropertyIsEitherComparedOrExcusedInWriting()
    {
        // The property this test exists for. A new setting must be a decision either way, not an
        // omission — which is how ExcludedLibraries came to be edited by a dialog that then did
        // nothing with the edit.
        var properties = PersistedProperties().ToList();

        // Guard against the reflection finding nothing and the test passing on an empty loop, which
        // is the only way a check of this shape fails silently.
        Assert.True(properties.Count > 20, $"only {properties.Count} persisted properties found");

        var unaccounted = new List<string>();

        foreach (var property in properties)
        {
            if (NotAboutFindings.ContainsKey(property.Name))
                continue;
            if (!ChangingItIsDetected(property))
                unaccounted.Add(property.Name);
        }

        Assert.True(unaccounted.Count == 0,
            "These persisted settings change nothing the dialog notices, and are not written down as " +
            "deliberate in NotAboutFindings: " + string.Join(", ", unaccounted));
    }

    [Fact]
    public void TheExcusedOnesReallyAreIgnored()
    {
        // The other direction: an entry in the list has to be true, or the list becomes a way of
        // silencing the test above.
        foreach (var (name, reason) in NotAboutFindings)
        {
            var property = typeof(StyleCheckingSettings).GetProperty(name);
            Assert.NotNull(property);
            Assert.False(ChangingItIsDetected(property!),
                $"'{name}' is excused as \"{reason}\" but changing it does trigger a re-check.");
        }
    }

    /// <summary>Mutates one property on a fresh object and asks whether the comparison sees it.</summary>
    private static bool ChangingItIsDetected(PropertyInfo property)
    {
        var baseline = new StyleCheckingSettings();
        var changed = new StyleCheckingSettings();
        Mutate(changed, property);
        return changed.ChecksDifferFrom(baseline);
    }

    private static void Mutate(StyleCheckingSettings settings, PropertyInfo property)
    {
        var value = property.GetValue(settings);

        switch (value)
        {
            case bool b when property.CanWrite:
                property.SetValue(settings, !b);
                return;
            case IList<string> list:
                // Removing from a defaulted list is as much a change as adding, and both have to be
                // seen — the excluded-library case that started this was a removal as often as an add.
                if (list.Count > 0) list.RemoveAt(list.Count - 1); else list.Add("Something");
                return;
            case IDictionary<string, RuleSeverity> severities:
                severities[RuleIds.ClassDescription] = RuleSeverity.Error;
                return;
            case NamingConventionSettings naming:
                naming.ExceptionNames.Add("Xyzzy");
                return;
        }

        throw new Xunit.Sdk.XunitException(
            $"No idea how to change '{property.Name}' ({property.PropertyType.Name}). Teach this test, " +
            "then decide whether the comparison should notice it.");
    }

    // ---- the specific case, stated on its own so a regression names itself -----------------------

    [Fact]
    public void AddingAnExcludedLibraryIsANoticedChange()
    {
        var before = new StyleCheckingSettings { ClassHasDescription = true };
        var after = new StyleCheckingSettings { ClassHasDescription = true };
        after.ExcludedLibraries.Add("Examples");

        Assert.True(after.ChecksDifferFrom(before));
    }

    [Fact]
    public void RemovingOneIsToo()
    {
        var before = new StyleCheckingSettings { ClassHasDescription = true };
        before.ExcludedLibraries.Add("Examples");
        var after = new StyleCheckingSettings { ClassHasDescription = true };

        Assert.True(after.ChecksDifferFrom(before));
    }

    [Fact]
    public void IdenticalSettingsDoNotDiffer()
    {
        var a = new StyleCheckingSettings { ClassHasDescription = true, ApplyFormattingRules = true };
        a.ExcludedLibraries.Add("Examples");
        var b = new StyleCheckingSettings { ClassHasDescription = true, ApplyFormattingRules = true };
        b.ExcludedLibraries.Add("Examples");

        Assert.False(a.ChecksDifferFrom(b));
        Assert.False(a.FormattingDiffersFrom(b));
    }

    [Fact]
    public void ChangingAnOrderingRuleIsAFormattingChange()
    {
        var before = new StyleCheckingSettings { OneOfEachSection = true, InitialEQAlgoFirst = true };
        var after = new StyleCheckingSettings { OneOfEachSection = true, InitialEQAlgoLast = true };

        Assert.True(after.FormattingDiffersFrom(before));
    }
}
