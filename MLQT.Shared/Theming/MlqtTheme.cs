using MLQT.Shared.Models;
using MudBlazor;

namespace MLQT.Shared.Theming;

/// <summary>
/// MLQT's MudBlazor theme: the default palettes, the palette built from a user's custom colours,
/// and the typography and layout every theme shares.
/// </summary>
/// <remarks>
/// <para>Lifted out of <c>MainLayout</c> in phase 7a-4. Four pure functions of a
/// <see cref="UISettings"/> had no business in a 3,000-line layout component, and while they were
/// there nothing could ask what colour the app would use without rendering the whole application
/// shell.</para>
///
/// <para>The typography is the part worth reading: MLQT deliberately runs smaller than MudBlazor's
/// defaults - body text at 0.75rem, headings scaled to match - because the windows this app fills
/// are dense with trees, tables and code. <c>Body2</c> is monospace, which is what makes it the
/// style CODING_GUIDELINES names for Modelica code and file paths.</para>
/// </remarks>
public static class MlqtTheme
{
    public static PaletteLight GetDefaultPaletteLight() => new PaletteLight()
    {
        Primary = "#6a70b1",
        Secondary = "#666666",
        Tertiary = "#a18ac1",
        TextPrimary = "#6a70b1",
        AppbarBackground = "#6a93b1",
        AppbarText = "#ffffff",
        Info = "#cccccc",
        PrimaryContrastText = "#ffffff"
    };

    public static PaletteDark GetDefaultPaletteDark() => new PaletteDark()
    {
        Primary = "#6a70b1",
        Secondary = "#666666",
        Tertiary = "#a18ac1",
        AppbarBackground = "#6a93b1",
        AppbarText = "#ffffff",
        Background = "#32333d"
    };

    public static PaletteLight BuildCustomPalette(UISettings uiSettings) => new PaletteLight()
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

    public static MudTheme BuildTheme(PaletteLight paletteLight) => new MudTheme()
    {
        PaletteLight = paletteLight,
        PaletteDark = GetDefaultPaletteDark(),
        LayoutProperties = new LayoutProperties()
        {
            DrawerWidthLeft = "200px",
            DrawerWidthRight = "200px",
            AppbarHeight = "2rem"
        },
        Typography = new Typography()
        {
            H4 = new DefaultTypography()
            {
                FontSize = "1.25rem",
                FontWeight = "500"
            },
            H5 = new DefaultTypography()
            {
                FontSize = "1.125rem",
                FontWeight = "500"
            },
            H6 = new DefaultTypography()
            {
                FontSize = "1rem",
                FontWeight = "500"
            },
            Body1 = new DefaultTypography()
            {
                FontSize = "0.75rem"
            },
            Body2 = new DefaultTypography()
            {
                FontFamily = new[] { "monospace"},
                FontSize = "0.75rem"
            },
            Subtitle1 = new DefaultTypography()
            {
                FontSize = "0.875rem",
                FontWeight = "500"
            },
            Subtitle2 = new DefaultTypography()
            {
                FontSize = "0.75rem",
                FontWeight = "500"
            },
            Caption = new DefaultTypography()
            {
                FontSize = "0.625rem"
            }
        }
    };
}
