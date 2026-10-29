using MudBlazor;

namespace MLQT.UI.Theme;

/// <summary>
/// MLQT's MudBlazor theme: the default palettes, and the typography and layout every theme shares.
/// </summary>
/// <remarks>
/// <para>Lifted out of <c>MainLayout</c> in phase 7a-4, and out of <c>MLQT.Shared</c> into this
/// package so that other tools can look like MLQT without referencing the application. The palette
/// built from a user's custom colours stayed behind as <c>MLQT.Shared.Theming.CustomPalette</c>:
/// it reads MLQT's settings, which are the application's business and not the theme's.</para>
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
        Info = "#6a70b1",
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
