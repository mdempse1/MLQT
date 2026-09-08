using MLQT.Shared.Models;
using MLQT.Shared.Theming;
using Xunit;

namespace MLQT.Shared.Tests.Theming;

/// <summary>
/// What choosing a theme does to the stored settings.
/// </summary>
/// <remarks>
/// Both defects these tests were written around are now fixed. B107 was a preset that quietly
/// discarded a hand-edited palette, and B108 two different answers to "is this palette hand-edited?".
/// The tests were first written to <i>pin</i> the surprising behaviour rather than change it during
/// an extraction, and they now assert the corrected behaviour instead — which is why several of them
/// describe what the code used to do.
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
    public void ChoosingLight_KeepsAHandEditedPalette()
    {
        // B107, fixed rather than pinned. This used to assert the reverse: selecting Light reset all
        // ten custom colours and SettingsUI persisted them immediately, so Custom -> Light -> Custom
        // came back to the defaults while Custom -> Dark -> Custom came back to the user's colours.
        var ui = new UISettings { CustomPrimary = "#ff0000", CustomBlack = "#010101" };

        ThemePresets.ApplyUiPreset("Light", ui);

        Assert.Equal("#ff0000", ui.CustomPrimary);
        Assert.Equal("#010101", ui.CustomBlack);
    }

    [Fact]
    public void NoPresetTouchesTheCustomPalette()
    {
        // The general form, so a future arm cannot quietly reintroduce the asymmetry for one preset.
        foreach (var preset in new[] { "Light", "Dark", "Custom", "Solarized", null })
        {
            var ui = new UISettings { CustomPrimary = "#ff0000", CustomInfo = "#00ff00" };

            ThemePresets.ApplyUiPreset(preset, ui);

            Assert.Equal("#ff0000", ui.CustomPrimary);
            Assert.Equal("#00ff00", ui.CustomInfo);
        }
    }

    [Fact]
    public void ChoosingDark_KeepsAHandEditedPalette()
    {
        // Dark never discarded the palette; this is here because it was the half that behaved
        // correctly, and it is what Light was made to match.
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
        // Since B107 this is exactly the same as passing "Light" - which is the point: the two used
        // to differ only in that one of them reset the palette.
        var ui = new UISettings { CustomPrimary = "#ff0000" };

        var custom = ThemePresets.ApplyUiPreset(name, ui);

        Assert.Equal(Theme.Light, ui.Theme);
        Assert.False(custom);
        Assert.Equal("#ff0000", ui.CustomPrimary);
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
