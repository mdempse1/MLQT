using MLQT.Shared.Models;
using MLQT.Shared.Theming;
using MudBlazor;
using MudBlazor.Utilities;
using Xunit;

namespace MLQT.Shared.Tests.Theming;

/// <summary>
/// <see cref="MlqtTheme"/>, lifted out of <c>MainLayout</c> in phase 7a-4.
///
/// <para>The first of the extractions, and the smallest on purpose: four pure functions with no
/// dependencies, so it proves the shape of the move before the analysis pipeline follows.</para>
/// </summary>
public class MlqtThemeTests
{
    private static UISettings Custom() => new()
    {
        Theme = Theme.Custom,
        CustomPrimary = "#112233",
        CustomPrimaryContrastText = "#ffffff",
        CustomSecondary = "#445566",
        CustomSecondaryContrastText = "#eeeeee",
        CustomTertiary = "#778899",
        CustomTertiaryContrastText = "#dddddd",
        CustomInfo = "#aabbcc",
        CustomInfoContrastText = "#cccccc",
        CustomBlack = "#000000",
        CustomWhite = "#ffffff",
    };

    /// <summary>
    /// Compares through <see cref="MudColor"/> rather than on the string: MudBlazor normalises what
    /// it is given (it carries an alpha channel), so "#32333d" is not what comes back out.
    /// </summary>
    private static void AssertColour(string expected, MudColor actual) =>
        Assert.Equal(new MudColor(expected).Value, actual.Value);

    [Fact]
    public void TheDefaultLightPalette_HasTheApplicationsOwnColours()
    {
        var palette = MlqtTheme.GetDefaultPaletteLight();

        AssertColour("#6a70b1", palette.Primary);
        AssertColour("#6a93b1", palette.AppbarBackground);
    }

    [Fact]
    public void TheDarkPalette_OverridesTheBackground()
    {
        // Without it the dark theme keeps MudBlazor's near-black, which does not match the app bar
        // MLQT keeps in both themes.
        AssertColour("#32333d", MlqtTheme.GetDefaultPaletteDark().Background);
    }

    [Fact]
    public void ACustomPalette_TakesEveryColourFromTheSettings()
    {
        var palette = MlqtTheme.BuildCustomPalette(Custom());

        AssertColour("#112233", palette.Primary);
        AssertColour("#445566", palette.Secondary);
        AssertColour("#778899", palette.Tertiary);
        AssertColour("#aabbcc", palette.Info);
    }

    [Fact]
    public void ACustomPalette_DrivesTheAppBarAndBodyTextFromThePrimaryColour()
    {
        // There is no separate setting for either, and a user who picks a dark primary and gets
        // black-on-black text has no way to correct it from the settings page.
        var palette = MlqtTheme.BuildCustomPalette(Custom());

        AssertColour("#112233", palette.AppbarBackground);
        AssertColour("#112233", palette.TextPrimary);
        AssertColour("#ffffff", palette.AppbarText);
    }

    [Fact]
    public void EveryThemeCarriesTheDarkPalette()
    {
        // The theme is built from a light palette, but MudBlazor switches to the dark one when the
        // user is in dark mode, so a theme without it renders unstyled in half the app's states.
        var theme = MlqtTheme.BuildTheme(MlqtTheme.GetDefaultPaletteLight());

        Assert.NotNull(theme.PaletteDark);
        AssertColour("#32333d", theme.PaletteDark.Background);
    }

    [Fact]
    public void TheThemeRunsSmallerThanMudBlazorsDefaults()
    {
        // Deliberate: the windows this app fills are dense with trees, tables and code. If these
        // ever revert to the framework defaults every panel in the application reflows.
        var theme = MlqtTheme.BuildTheme(MlqtTheme.GetDefaultPaletteLight());

        Assert.Equal("0.75rem", theme.Typography.Body1.FontSize);
        Assert.Equal("2rem", theme.LayoutProperties.AppbarHeight);
    }

    [Fact]
    public void Body2IsMonospace()
    {
        // CODING_GUIDELINES names body2 as the style for Modelica code and file paths, which only
        // works because it is monospace here.
        var theme = MlqtTheme.BuildTheme(MlqtTheme.GetDefaultPaletteLight());

        Assert.Contains("monospace", theme.Typography.Body2.FontFamily!);
    }

    [Fact]
    public void ACustomThemeAndTheDefaultTheme_DifferOnlyInTheirLightPalette()
    {
        // The characterisation this extraction needed: MainLayout chooses between two palettes and
        // builds the theme the same way from either, so everything except PaletteLight has to match.
        var standard = MlqtTheme.BuildTheme(MlqtTheme.GetDefaultPaletteLight());
        var custom = MlqtTheme.BuildTheme(MlqtTheme.BuildCustomPalette(Custom()));

        Assert.NotEqual(standard.PaletteLight.Primary.Value, custom.PaletteLight.Primary.Value);
        Assert.Equal(standard.PaletteDark.Background.Value, custom.PaletteDark.Background.Value);
        Assert.Equal(standard.Typography.Body1.FontSize, custom.Typography.Body1.FontSize);
        Assert.Equal(standard.LayoutProperties.AppbarHeight, custom.LayoutProperties.AppbarHeight);
    }
}
