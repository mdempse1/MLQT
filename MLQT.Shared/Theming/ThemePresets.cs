using MLQT.Shared.Models;

namespace MLQT.Shared.Theming;

/// <summary>
/// What choosing a named theme in the settings page does to the stored settings.
/// </summary>
/// <remarks>
/// <para>Extracted from <c>SettingsUI</c> in 7a-3. The rules were reachable only by rendering the
/// settings page and clicking a select, which is why nothing had ever asked what they do to a
/// user's saved colours — and two of them do something surprising. See
/// <see cref="ApplyUiPreset"/> and <see cref="IsCustomSyntax"/>.</para>
/// </remarks>
public static class ThemePresets
{
    /// <summary>The theme name that means "the user is editing the colours by hand".</summary>
    public const string CustomThemeName = "Custom";

    /// <summary>
    /// Applies a named UI preset to <paramref name="ui"/> and says whether the custom palette editor
    /// belongs on screen.
    /// </summary>
    /// <remarks>
    /// <para><b>Choosing "Light" resets the custom palette</b>, and choosing "Dark" does not. That is
    /// the behaviour as found, pinned by a test rather than tidied: it decides what happens to
    /// colours a user typed in, and changing that is a product decision, not a refactor. Its visible
    /// consequence is that Custom → Light → Custom comes back to the defaults while
    /// Custom → Dark → Custom comes back to the user's colours. Recorded as B107.</para>
    ///
    /// <para>An unrecognised name falls back to Light <i>without</i> the reset, so it is not the same
    /// as passing "Light" — also pinned, also B107.</para>
    /// </remarks>
    /// <returns><c>true</c> when the theme is <see cref="Theme.Custom"/> and the palette editor shows.</returns>
    public static bool ApplyUiPreset(string? themeName, UISettings ui)
    {
        switch (themeName)
        {
            case "Light":
                ui.Theme = Theme.Light;
                ResetCustomPaletteToDefaults(ui);
                return false;

            case "Dark":
                ui.Theme = Theme.Dark;
                return false;

            case CustomThemeName:
                ui.Theme = Theme.Custom;
                return true;

            default:
                ui.Theme = Theme.Light;
                return false;
        }
    }

    /// <summary>
    /// Puts the ten custom palette colours back to the values a fresh <see cref="UISettings"/> has.
    /// </summary>
    /// <remarks>
    /// Copied from a default instance rather than written out again. The settings page held its own
    /// literal copy of all ten, identical to the property initialisers and held to them by nothing —
    /// so changing a default would have changed what a new user sees and not what the Light preset
    /// restores, which is the more confusing half of that pair.
    /// </remarks>
    public static void ResetCustomPaletteToDefaults(UISettings ui)
    {
        var defaults = new UISettings();

        ui.CustomBlack = defaults.CustomBlack;
        ui.CustomWhite = defaults.CustomWhite;
        ui.CustomPrimary = defaults.CustomPrimary;
        ui.CustomPrimaryContrastText = defaults.CustomPrimaryContrastText;
        ui.CustomSecondary = defaults.CustomSecondary;
        ui.CustomSecondaryContrastText = defaults.CustomSecondaryContrastText;
        ui.CustomTertiary = defaults.CustomTertiary;
        ui.CustomTertiaryContrastText = defaults.CustomTertiaryContrastText;
        ui.CustomInfo = defaults.CustomInfo;
        ui.CustomInfoContrastText = defaults.CustomInfoContrastText;
    }

    /// <summary>
    /// The syntax highlighting settings for a named preset, at the given light/dark mode.
    /// </summary>
    /// <remarks>
    /// An unrecognised name — including <see cref="CustomThemeName"/> — keeps the current colours and
    /// only restamps the name. That is what makes "Custom" survive a light/dark switch: the presets
    /// are recomputed for the new mode, and the hand-edited palette is left alone.
    /// </remarks>
    public static SyntaxHighlightingSettings SyntaxThemeFor(
        string? themeName,
        bool darkMode,
        SyntaxHighlightingSettings current)
    {
        var settings = themeName switch
        {
            "Dymola" => SyntaxHighlightingSettings.GetDymolaTheme(darkMode),
            "OpenModelica" => SyntaxHighlightingSettings.GetOpenModelicaTheme(darkMode),
            "VSCode" => darkMode ? SyntaxHighlightingSettings.GetDarkTheme() : SyntaxHighlightingSettings.GetLightTheme(),
            _ => current,
        };

        settings.ThemeName = themeName ?? "";
        return settings;
    }

    /// <summary>
    /// Whether a syntax theme name means the hand-edited palette, and so whether the colour pickers
    /// belong on screen.
    /// </summary>
    /// <remarks>
    /// <para><b>The single answer to that question, which is the fix for B108.</b> The settings page
    /// asked it two ways: on load it read the stored name, and after applying a preset it set the
    /// flag to <c>false</c> unconditionally. Changing the UI theme re-applies the syntax preset, so
    /// switching light↔dark while on a custom syntax palette turned the colour pickers off while the
    /// stored name was still "Custom" — the settings said Custom, the page showed presets, and
    /// reopening the page brought the pickers back.</para>
    /// </remarks>
    public static bool IsCustomSyntax(string? themeName) =>
        themeName == CustomThemeName;
}
