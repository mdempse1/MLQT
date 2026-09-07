using MLQT.Shared.Models;
using MLQT.Shared.Theming;
using Xunit;

namespace MLQT.Shared.Tests.Theming;

/// <summary>
/// What choosing a theme does to the stored settings.
/// </summary>
/// <remarks>
/// Two of these tests pin behaviour rather than endorse it, and say so where they do. Both decide
/// what happens to colours a user typed in by hand, which is a product decision and not something to
/// change while extracting a method — B107 and B108 in the backlog.
/// </remarks>
public class ThemePresetsTests
{
    // ---- the UI presets --------------------------------------------------------------------

    [Fact]
    public void Light_SelectsTheLightTheme_AndHidesThePaletteEditor()
    {
        var ui = new UISettings();

        var custom = ThemePresets.ApplyUiPreset("Light", ui);

        Assert.Equal(Theme.Light, ui.Theme);
        Assert.False(custom);
    }

    [Fact]
    public void Dark_SelectsTheDarkTheme_AndHidesThePaletteEditor()
    {
        var ui = new UISettings();

        var custom = ThemePresets.ApplyUiPreset("Dark", ui);

        Assert.Equal(Theme.Dark, ui.Theme);
        Assert.False(custom);
    }

    [Fact]
    public void Custom_SelectsTheCustomTheme_AndShowsThePaletteEditor()
    {
        var ui = new UISettings();

        var custom = ThemePresets.ApplyUiPreset("Custom", ui);

        Assert.Equal(Theme.Custom, ui.Theme);
        Assert.True(custom);
    }

    [Fact]
    public void ChoosingLight_DiscardsAHandEditedPalette()
    {
        // B107, pinned rather than fixed. A user who has built a custom palette, switched to Light to
        // compare, and switched back to Custom does not get their colours back - selecting Light
        // overwrote them, and SettingsUI persists immediately afterwards, so they are gone from disk
        // too. Stated as a test so that the behaviour is a decision somebody can look at.
        var ui = new UISettings { CustomPrimary = "#ff0000", CustomBlack = "#010101" };

        ThemePresets.ApplyUiPreset("Light", ui);

        Assert.Equal(new UISettings().CustomPrimary, ui.CustomPrimary);
        Assert.Equal(new UISettings().CustomBlack, ui.CustomBlack);
    }

    [Fact]
    public void ChoosingDark_KeepsAHandEditedPalette()
    {
        // The other half of B107: the same round trip through Dark keeps the colours. Two preset
        // buttons side by side, one destructive and one not, with nothing on screen to say so.
        var ui = new UISettings { CustomPrimary = "#ff0000" };

        ThemePresets.ApplyUiPreset("Dark", ui);

        Assert.Equal("#ff0000", ui.CustomPrimary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Solarized")]
    public void AnUnrecognisedName_FallsBackToLightButKeepsThePalette(string? name)
    {
        // Also B107: this is *not* the same as passing "Light", which resets. Pinned because the
        // difference is invisible in the source - one arm calls the reset and the other does not.
        var ui = new UISettings { CustomPrimary = "#ff0000" };

        var custom = ThemePresets.ApplyUiPreset(name, ui);

        Assert.Equal(Theme.Light, ui.Theme);
        Assert.False(custom);
        Assert.Equal("#ff0000", ui.CustomPrimary);
    }

    [Fact]
    public void TheResetRestoresEveryCustomColour()
    {
        // All ten, not the two the other tests happen to name. The settings page used to carry its
        // own literal copy of these values, so a default changed in AppSettings.cs and not there
        // would have made "restore defaults" restore something else.
        var ui = new UISettings
        {
            CustomBlack = "#1", CustomWhite = "#2", CustomPrimary = "#3",
            CustomPrimaryContrastText = "#4", CustomSecondary = "#5",
            CustomSecondaryContrastText = "#6", CustomTertiary = "#7",
            CustomTertiaryContrastText = "#8", CustomInfo = "#9", CustomInfoContrastText = "#10",
        };

        ThemePresets.ResetCustomPaletteToDefaults(ui);

        var defaults = new UISettings();
        Assert.Equal(defaults.CustomBlack, ui.CustomBlack);
        Assert.Equal(defaults.CustomWhite, ui.CustomWhite);
        Assert.Equal(defaults.CustomPrimary, ui.CustomPrimary);
        Assert.Equal(defaults.CustomPrimaryContrastText, ui.CustomPrimaryContrastText);
        Assert.Equal(defaults.CustomSecondary, ui.CustomSecondary);
        Assert.Equal(defaults.CustomSecondaryContrastText, ui.CustomSecondaryContrastText);
        Assert.Equal(defaults.CustomTertiary, ui.CustomTertiary);
        Assert.Equal(defaults.CustomTertiaryContrastText, ui.CustomTertiaryContrastText);
        Assert.Equal(defaults.CustomInfo, ui.CustomInfo);
        Assert.Equal(defaults.CustomInfoContrastText, ui.CustomInfoContrastText);
    }

    [Fact]
    public void TheResetDoesNotTouchTheThemeItself()
    {
        // It is called from the Light arm, which has already set the theme. A reset that also reset
        // the theme would make the method unusable from anywhere else.
        var ui = new UISettings { Theme = Theme.Dark };

        ThemePresets.ResetCustomPaletteToDefaults(ui);

        Assert.Equal(Theme.Dark, ui.Theme);
    }

    // ---- the syntax presets ----------------------------------------------------------------

    [Theory]
    [InlineData("Dymola")]
    [InlineData("OpenModelica")]
    [InlineData("VSCode")]
    public void ANamedSyntaxPreset_ReplacesTheColours(string name)
    {
        var current = new SyntaxHighlightingSettings { KeywordColor = "#ff0000", ThemeName = "Custom" };

        var result = ThemePresets.SyntaxThemeFor(name, darkMode: false, current);

        Assert.Equal(name, result.ThemeName);
        Assert.NotEqual("#ff0000", result.KeywordColor);
    }

    [Theory]
    [InlineData("Dymola")]
    [InlineData("OpenModelica")]
    [InlineData("VSCode")]
    public void EachSyntaxPreset_DiffersBetweenLightAndDark(string name)
    {
        // The reason ApplyPresetUITheme re-applies the syntax theme when the UI theme changes: a
        // light code background under a dark chrome is the bug this exists to avoid. If a preset
        // ignored darkMode, that re-application would be pointless work nobody would notice.
        var light = ThemePresets.SyntaxThemeFor(name, darkMode: false, new SyntaxHighlightingSettings());
        var dark = ThemePresets.SyntaxThemeFor(name, darkMode: true, new SyntaxHighlightingSettings());

        Assert.NotEqual(light.BackgroundColor, dark.BackgroundColor);
    }

    [Fact]
    public void TheCustomSyntaxTheme_KeepsTheUsersColoursThroughAThemeSwitch()
    {
        // The point of the fallback arm. Switching the UI between light and dark re-applies the
        // syntax theme; a hand-edited palette has to come through that untouched.
        var current = new SyntaxHighlightingSettings { KeywordColor = "#abcdef", ThemeName = "Custom" };

        var result = ThemePresets.SyntaxThemeFor("Custom", darkMode: true, current);

        Assert.Equal("#abcdef", result.KeywordColor);
        Assert.Equal("Custom", result.ThemeName);
    }

    [Fact]
    public void AnUnknownSyntaxName_KeepsTheColoursAndTakesTheName()
    {
        var current = new SyntaxHighlightingSettings { KeywordColor = "#abcdef" };

        var result = ThemePresets.SyntaxThemeFor("Nonsense", darkMode: false, current);

        Assert.Equal("#abcdef", result.KeywordColor);
        Assert.Equal("Nonsense", result.ThemeName);
    }

    // ---- IsCustomSyntax (B108) -------------------------------------------------------------

    [Fact]
    public void OnlyTheCustomName_MeansTheColourPickers()
    {
        Assert.True(ThemePresets.IsCustomSyntax("Custom"));

        foreach (var other in new[] { "Dymola", "OpenModelica", "VSCode", "", "custom", null })
            Assert.False(ThemePresets.IsCustomSyntax(other), $"{other ?? "null"} should not show the pickers");
    }

    [Fact]
    public void ACustomSyntaxTheme_SurvivesAUiThemeChange()
    {
        // B108 as the user meets it: on a custom syntax palette, switch the UI from light to dark and
        // the colour pickers used to vanish while the stored theme name still said Custom - so the
        // settings and the screen disagreed until the page was reopened. The two questions are now
        // one function, and this is the round trip that used to break.
        var current = new SyntaxHighlightingSettings { KeywordColor = "#abcdef", ThemeName = "Custom" };

        var afterSwitch = ThemePresets.SyntaxThemeFor(current.ThemeName, darkMode: true, current);

        Assert.True(ThemePresets.IsCustomSyntax(afterSwitch.ThemeName));
    }
}
