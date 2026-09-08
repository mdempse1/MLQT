using System.Text.RegularExpressions;
using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The one-time copy of a user's settings out of MAUI's Preferences (phase 7b-3).
/// </summary>
/// <remarks>
/// This runs once, on one machine, on the day somebody upgrades, and if it is wrong they lose their
/// project list and find out later. It cannot be tested by running it — MAUI's Preferences needs a
/// MAUI app — so the decision is a function over two stores and the delegates are the seam.
/// </remarks>
public class MauiPreferencesSeedTests
{
    private sealed class Store
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public string? Read(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Write(string key, string value) => Values[key] = value;
    }

    [Fact]
    public void ItCopiesEveryKnownKeyTheOldStoreHas()
    {
        var old = new Store();
        foreach (var key in MlqtSettingsKeys.All)
            old.Values[key] = $"value-of-{key}";

        var fresh = new Store();

        var copied = MauiPreferencesSeed.Seed(old.Read, fresh.Read, fresh.Write, alreadySeeded: false);

        Assert.Equal(MlqtSettingsKeys.All.Order(), copied.Order());
        foreach (var key in MlqtSettingsKeys.All)
            Assert.Equal($"value-of-{key}", fresh.Read(key));
    }

    [Fact]
    public void AKeyTheOldStoreDoesNotHave_IsSkipped()
    {
        // A user who never opened the external tools page has no Dymola setting, and writing an
        // empty one would be worse than leaving it absent - the app treats absent as "use defaults".
        var old = new Store();
        old.Values[MlqtSettingsKeys.Ui] = "theme";
        var fresh = new Store();

        var copied = MauiPreferencesSeed.Seed(old.Read, fresh.Read, fresh.Write, alreadySeeded: false);

        Assert.Equal([MlqtSettingsKeys.Ui], copied);
        Assert.Single(fresh.Values);
    }

    [Fact]
    public void AKeyTheNewStoreAlreadyHas_IsNotOverwritten()
    {
        // The case that loses work. A user runs the new host, changes their theme, then opens the old
        // one once more - and the seed must not put the old theme back. Anything in the new store is
        // newer by definition, because the old host cannot write there.
        var old = new Store();
        old.Values[MlqtSettingsKeys.Ui] = "old-theme";
        var fresh = new Store();
        fresh.Values[MlqtSettingsKeys.Ui] = "theme-the-user-just-chose";

        var copied = MauiPreferencesSeed.Seed(old.Read, fresh.Read, fresh.Write, alreadySeeded: false);

        Assert.Empty(copied);
        Assert.Equal("theme-the-user-just-chose", fresh.Read(MlqtSettingsKeys.Ui));
    }

    [Fact]
    public void OnceSeeded_ItNeverRunsAgain()
    {
        // The second guard, and it guards something the per-key check does not: a setting the user
        // deleted in the new host would otherwise come back every time the old host was opened.
        var old = new Store();
        old.Values[MlqtSettingsKeys.Repositories] = "the old repository list";
        var fresh = new Store();

        var copied = MauiPreferencesSeed.Seed(old.Read, fresh.Read, fresh.Write, alreadySeeded: true);

        Assert.Empty(copied);
        Assert.Empty(fresh.Values);
    }

    [Fact]
    public void ItNeverWritesToTheOldStore()
    {
        // Preferences is left exactly as it was so that going back to a previous MLQT release still
        // works. During a migration the rollback has to work, which matters more than tidiness.
        var old = new Store();
        old.Values[MlqtSettingsKeys.Ui] = "theme";
        var before = new Dictionary<string, string>(old.Values);

        MauiPreferencesSeed.Seed(old.Read, new Store().Read, (_, _) => { }, alreadySeeded: false);

        Assert.Equal(before, old.Values);
    }

    [Fact]
    public void AnEmptyValueIsTreatedAsAbsent()
    {
        // Preferences answers with the default for a key it does not hold, and the MAUI service's
        // default is the empty string rather than null. Copying that would write "" over a setting
        // the new host would otherwise fill with a real default.
        var old = new Store();
        old.Values[MlqtSettingsKeys.Ui] = "";
        var fresh = new Store();

        var copied = MauiPreferencesSeed.Seed(old.Read, fresh.Read, fresh.Write, alreadySeeded: false);

        Assert.Empty(copied);
    }

    // ---- the catalogue, held to the code ----------------------------------------------------

    [Fact]
    public void TheKeyCatalogueMatchesTheKeysTheCodebaseActuallyUses()
    {
        // MAUI's Preferences cannot be enumerated, so the seed can only move keys that are written
        // down - and a key nobody wrote down is a setting that silently does not survive the
        // migration, for every existing user, on the day they upgrade. This is the guard.
        var root = RepositoryRoot();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                     .Where(f => !f.Contains(".Tests")))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file),
                         @"Settings(?:Service)?\.(?:Get|Set|Remove)Async(?:<[^>]*>)?\(\s*""([^""]+)"""))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        Assert.True(used.Count >= 5, $"only found {used.Count} settings keys in the source; the pattern may have gone stale");

        var missing = used.Except(MlqtSettingsKeys.All, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            "these settings keys are used in the codebase and are not in MlqtSettingsKeys, so they "
            + "would not survive the migration from MAUI Preferences: " + string.Join(", ", missing));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }
}
