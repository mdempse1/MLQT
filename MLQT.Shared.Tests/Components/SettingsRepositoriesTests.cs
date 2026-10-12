using MLQT.Shared.Components;
using ModelicaGraph;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// <see cref="SettingsRepositories.EffectOfEdit"/> — the two booleans an edit to a repository's
/// settings hands to <c>AppState.RepositorySettingsApplied</c>.
///
/// <para><b>Why this first.</b> Those booleans decide whether every file in the repository is
/// rewritten by the formatter and every class re-checked. Getting either wrong is expensive when it
/// says yes and invisible when it says no, and until phase 7a-1 moved this out of an <c>@code</c>
/// block nothing could call it without rendering the settings panel.</para>
/// </summary>
public class SettingsRepositoriesTests
{
    /// <summary>Settings with formatting on and one rule enabled, as a repository in use would be.</summary>
    private static StyleCheckingSettings Configured() => new()
    {
        ApplyFormattingRules = true,
        RuleSeverities = { [RuleIds.OneOfEachSection] = RuleSeverity.Warning },
    };

    [Fact]
    public void EffectOfEdit_WithNoChange_RequiresNothing()
    {
        var (formatting, styleChecks) = SettingsRepositories.EffectOfEdit(Configured(), Configured());

        Assert.False(formatting);
        Assert.False(styleChecks);
    }

    [Fact]
    public void EffectOfEdit_WhenARuleSeverityChanges_RequiresReChecking()
    {
        var before = Configured();
        var after = Configured();
        after.RuleSeverities[RuleIds.OneOfEachSection] = RuleSeverity.Error;

        var (formatting, styleChecks) = SettingsRepositories.EffectOfEdit(before, after);

        Assert.True(styleChecks);
        Assert.False(formatting);
    }

    [Fact]
    public void EffectOfEdit_WhenAFormattingRuleChanges_RequiresReformatting()
    {
        var before = Configured();
        var after = Configured();
        after.OneOfEachSection = !before.OneOfEachSection;

        var (formatting, _) = SettingsRepositories.EffectOfEdit(before, after);

        Assert.True(formatting);
    }

    [Fact]
    public void EffectOfEdit_WhenAFormattingRuleChangesButFormattingIsOff_RequiresNoReformatting()
    {
        // The rule that is easy to lose: the formatter will not run at all, so there is nothing to
        // redo, however different the layout options now are.
        var before = new StyleCheckingSettings { ApplyFormattingRules = false };
        var after = new StyleCheckingSettings { ApplyFormattingRules = false };
        after.OneOfEachSection = !before.OneOfEachSection;

        var (formatting, _) = SettingsRepositories.EffectOfEdit(before, after);

        Assert.False(formatting);
    }

    [Fact]
    public void EffectOfEdit_WhenFormattingIsTurnedOn_RequiresReformattingEvenWithNoRuleChange()
    {
        // The other easy one, in the opposite direction: no formatting rule was touched, but the
        // files in this repository have never been written under them, so they all need rewriting.
        var before = new StyleCheckingSettings { ApplyFormattingRules = false };
        var after = new StyleCheckingSettings { ApplyFormattingRules = true };

        var (formatting, _) = SettingsRepositories.EffectOfEdit(before, after);

        Assert.True(formatting);
    }

    [Fact]
    public void EffectOfEdit_WhenFormattingIsTurnedOff_RequiresNoReformatting()
    {
        var before = new StyleCheckingSettings { ApplyFormattingRules = true };
        var after = new StyleCheckingSettings { ApplyFormattingRules = false };

        var (formatting, _) = SettingsRepositories.EffectOfEdit(before, after);

        Assert.False(formatting);
    }

    [Fact]
    public void EffectOfEdit_TogglingFormatting_AlsoRequiresReChecking()
    {
        // Not obvious from this method, and worth pinning: ApplyFormattingRules is one of the
        // properties ChecksDifferFrom compares, because the formatter closes several findings and
        // turning it on or off changes which ones a class should report.
        var before = new StyleCheckingSettings { ApplyFormattingRules = false };
        var after = new StyleCheckingSettings { ApplyFormattingRules = true };

        var (_, styleChecks) = SettingsRepositories.EffectOfEdit(before, after);

        Assert.True(styleChecks);
    }

    [Fact]
    public void EffectOfEdit_WhenAnExcludedLibraryIsAdded_RequiresReChecking()
    {
        var before = Configured();
        var after = Configured();
        after.ExcludedLibraries.Add("Modelica");

        var (_, styleChecks) = SettingsRepositories.EffectOfEdit(before, after);

        Assert.True(styleChecks);
    }

    [Fact]
    public void EffectOfEdit_WhenASpellCheckLanguageIsAdded_RequiresReChecking()
    {
        var before = Configured();
        var after = Configured();
        after.SpellCheckLanguages.Add("en_GB");

        var (_, styleChecks) = SettingsRepositories.EffectOfEdit(before, after);

        Assert.True(styleChecks);
    }

    /// <summary>
    /// <see cref="SettingsRepositories.BackupOf"/> — what Cancel puts back. The dialog edits the
    /// repository's own settings object, so a backup sharing it, or any collection in it, would
    /// "restore" the edit it was taken to undo, and <see cref="SettingsRepositories.EffectOfEdit"/>
    /// would compare the edited settings with themselves and ask for nothing to be redone.
    /// </summary>
    [Fact]
    public void BackupOf_KeepsTheSettingsAsTheyWere_WhenTheRepositorysAreEdited()
    {
        var settings = Configured();
        settings.ExcludedLibraries.Add("Modelica");
        settings.FormattingExcludedModels.Add("Lib.Generated");
        var repository = new MLQT.Services.DataTypes.Repository
        {
            Name = "Lib", RemotePath = "https://example.invalid/lib.git", LocalPath = "lib",
            IsReferenceOnly = true, StyleSettings = settings,
        };

        var backup = SettingsRepositories.BackupOf(repository);

        Assert.NotSame(settings, backup.StyleSettings);
        var (formatting, styleChecks) = SettingsRepositories.EffectOfEdit(backup.StyleSettings!, settings);
        Assert.False(formatting);
        Assert.False(styleChecks);

        settings.RuleSeverities[RuleIds.OneOfEachSection] = RuleSeverity.Error;
        settings.ExcludedLibraries.Add("Buildings");
        settings.FormattingExcludedModels.Clear();
        settings.ApplyFormattingRules = false;

        Assert.Equal(RuleSeverity.Warning, backup.StyleSettings!.RuleSeverities[RuleIds.OneOfEachSection]);
        Assert.Equal(["Modelica"], backup.StyleSettings.ExcludedLibraries);
        Assert.Equal(["Lib.Generated"], backup.StyleSettings.FormattingExcludedModels);
        Assert.True(backup.StyleSettings.ApplyFormattingRules);
        Assert.Equal(("Lib", "https://example.invalid/lib.git", "lib", true),
            (backup.Name, backup.RemotePath, backup.LocalPath, backup.IsReferenceOnly));
    }

    [Fact]
    public void BackupOf_ARepositoryWithNoSettings_HasNone()
    {
        var backup = SettingsRepositories.BackupOf(new MLQT.Services.DataTypes.Repository());

        Assert.Null(backup.StyleSettings);
    }
}
