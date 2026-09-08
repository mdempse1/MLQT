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
    /// <para><b>No preset touches the custom palette</b> (B107). Choosing "Light" used to reset all ten
    /// custom colours, and <c>SettingsUI</c> persists immediately afterwards, so a user who had built
    /// a palette, switched to Light to compare, and switched back found the defaults - while the same
    /// round trip through Dark kept their colours. Two buttons side by side, one destructive, with
    /// nothing on screen to say so.</para>
    ///
    /// <para>The switch is written so that cannot come back: Light, Dark and an unrecognised name
    /// differ only in which <see cref="Theme"/> they select, and there is no arm left for one of them
    /// to do something extra in. The custom colours are the user's, and a preset only decides which
    /// palette is *shown*.</para>
    /// </remarks>
    /// <returns><c>true</c> when the theme is <see cref="Theme.Custom"/> and the palette editor shows.</returns>
    public static bool ApplyUiPreset(string? themeName, UISettings ui)
    {
        ui.Theme = themeName switch
        {
            "Dark" => Theme.Dark,
            CustomThemeName => Theme.Custom,

            // "Light" and anything unrecognised. They were separate arms while one of them reset the
            // palette and the other did not, which is half of what made B107 hard to see.
            _ => Theme.Light,
        };

        return ui.Theme == Theme.Custom;
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
