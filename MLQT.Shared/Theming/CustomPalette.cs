using MLQT.Shared.Models;
using MudBlazor;

namespace MLQT.Shared.Theming;

/// <summary>
/// The light palette built from the custom colours a user chose in the UI settings.
/// </summary>
/// <remarks>
/// It stays here, not in <c>MLQT.UI.Theme</c> with the rest of <see cref="MLQT.UI.Theme.MlqtTheme"/>,
/// because it reads <see cref="UISettings"/>: the theme package must not depend on MLQT's settings,
/// or every tool that uses it would carry the application along with it. The result goes to
/// <see cref="MLQT.UI.Theme.MlqtTheme.BuildTheme"/> like either default palette.
/// </remarks>
public static class CustomPalette
{
    public static PaletteLight FromSettings(UISettings uiSettings) => new PaletteLight()
    {
        Black = uiSettings.CustomBlack,
        White = uiSettings.CustomWhite,
        Primary = uiSettings.CustomPrimary,
        PrimaryContrastText = uiSettings.CustomPrimaryContrastText,
        Secondary = uiSettings.CustomSecondary,
        SecondaryContrastText = uiSettings.CustomSecondaryContrastText,
        Tertiary = uiSettings.CustomTertiary,
        TertiaryContrastText = uiSettings.CustomTertiaryContrastText,
        Info = uiSettings.CustomInfo,
        InfoContrastText = uiSettings.CustomInfoContrastText,
        TextPrimary = uiSettings.CustomPrimary,
        AppbarBackground = uiSettings.CustomPrimary,
        AppbarText = uiSettings.CustomPrimaryContrastText
    };
}
