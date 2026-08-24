using ModelicaGraph;
using DymolaInterface;
using OpenModelicaInterface;

namespace MLQT.Shared.Models;

/// <summary>
/// Root settings model for the application
/// </summary>
public class AppSettings
{
    public UISettings UI { get; set; } = new();
    public SyntaxHighlightingSettings SyntaxHighlighting { get; set; } = new();
    public DymolaSettings Dymola { get; set; } = new();
    public OpenModelicaSettings OpenModelica { get; set; } = new();
    public StyleCheckingSettings StyleChecking { get; set; } = new();
    public ReferenceLibrarySettings ReferenceLibraries { get; set; } = new();
}

/// <summary>
/// Libraries loaded read-only so that references out of the user's own code resolve — typically a
/// tool's installed library folder such as Dymola's <c>Modelica\Library</c>.
///
/// <para>These are held in application settings rather than in a repository's
/// <c>.mlqt/settings.json</c> because the paths are properties of the machine, not of the code. A
/// checkout shared with colleagues or a CI runner would not have the same install location, and
/// baking one into the repository would break for everyone else. CI supplies the equivalent
/// explicitly with the CLI's <c>--dependency</c> option.</para>
/// </summary>
public class ReferenceLibrarySettings
{
    /// <summary>
    /// Directories to scan for reference libraries. Each may be a single library or a folder
    /// containing many; both readable and encrypted libraries are picked up.
    /// </summary>
    public List<string> Paths { get; set; } = new();

    /// <summary>
    /// Whether to recover the classes of encrypted libraries from their vendor documentation.
    /// Turning this off leaves those namespaces opaque — references into them stay unresolved and
    /// inherited icons stay invisible, which is the behaviour from before that was possible.
    /// </summary>
    public bool UseEncryptedLibraryDocumentation { get; set; } = true;
}

/// <summary>
/// UI-related settings
/// </summary>
public class UISettings
{
    public Theme Theme { get; set; } = Theme.Light;
    public bool RepositoryMode { get; set; } = true;

    /// <summary>
    /// Model count threshold above which startup analysis steps (formatting, dependencies,
    /// style checking, external resources) are deferred until the user triggers them.
    /// Set to 0 to always run all steps on startup.
    /// </summary>
    public int DeferAnalysisThreshold { get; set; } = 500;

    // Custom palette colors (used when Theme == Custom, defaults match the default PaletteLight theme)
    public string CustomBlack { get; set; } = "#272c34";
    public string CustomWhite { get; set; } = "#ffffff";
    public string CustomPrimary { get; set; } = "#6a70b1";
    public string CustomPrimaryContrastText { get; set; } = "#ffffff";
    public string CustomSecondary { get; set; } = "#666666";
    public string CustomSecondaryContrastText { get; set; } = "#ffffff";
    public string CustomTertiary { get; set; } = "#a18ac1";
    public string CustomTertiaryContrastText { get; set; } = "#ffffff";
    public string CustomInfo { get; set; } = "#cccccc";
    public string CustomInfoContrastText { get; set; } = "#ffffff";
}

public enum Theme
{
    Light, 
    Dark, 
    Custom
}

/// <summary>
/// Syntax highlighting color settings for the CodeViewer component
/// </summary>
public class SyntaxHighlightingSettings
{
    public string ThemeName { get; set; } = "VSCode";
    
    // Editor background and text colors
    public string BackgroundColor { get; set; } = "#ffffff";
    public string TextColor { get; set; } = "#000000";
    public string BorderColor { get; set; } = "#e1e4e8";

    // Syntax element colors (matching CodeViewer.razor.css classes)
    public string KeywordColor { get; set; } = "#0000ff";      // Blue for keywords (model, end, etc.)
    public string TypeColor { get; set; } = "#267f99";         // Teal for types
    public string IdentColor { get; set; } = "#001080";        // Dark blue for identifiers
    public string NameColor { get; set; } = "#001080";         // Dark blue for names
    public string FunctionColor { get; set; } = "#99268c";        // Dark blue for identifiers
    public string OperatorColor { get; set; } = "#000000";     // Black for operators
    public string NumberColor { get; set; } = "#098658";       // Green for numbers
    public string StringColor { get; set; } = "#a31515";       // Red for strings
    public string CommentColor { get; set; } = "#559983";      // Gray-green for comments
    public string LineNumberColor { get; set; } = "#777777";

    /// <summary>
    /// Get a predefined theme
    /// </summary>
    public static SyntaxHighlightingSettings GetLightTheme()
    {
        return new SyntaxHighlightingSettings
        {
            BackgroundColor = "#ffffff",
            TextColor = "#000000",
            BorderColor = "#e1e4e8",
            KeywordColor = "#0000ff",
            TypeColor = "#267f99",
            IdentColor = "#001080",
            NameColor = "#001080",
            FunctionColor = "#267f99",
            OperatorColor = "#000000",
            NumberColor = "#098658",
            StringColor = "#a31515",
            CommentColor = "#559983",
            LineNumberColor = "#777777"
        };
    }

    /// <summary>
    /// Get VS Code Dark+ inspired theme
    /// </summary>
    public static SyntaxHighlightingSettings GetDarkTheme()
    {
        return new SyntaxHighlightingSettings
        {
            BackgroundColor = "#32333d",
            TextColor = "#d4d4d4",
            BorderColor = "#3e3e42",
            KeywordColor = "#569cd6",
            TypeColor = "#4ec9b0",
            IdentColor = "#9cdcfe",
            NameColor = "#9cdcfe",
            FunctionColor = "#4ec9b0",
            OperatorColor = "#d4d4d4",
            NumberColor = "#b5cea8",
            StringColor = "#ce9178",
            CommentColor = "#6a9955",
            LineNumberColor = "#dddddd"
        };
    }

    /// <summary>
    /// Get Solarized Light theme
    /// </summary>
    public static SyntaxHighlightingSettings GetDymolaTheme(bool darkMode)
    {
        return new SyntaxHighlightingSettings
        {
            BackgroundColor = darkMode ? "#32333d" : "#ffffff",
            TextColor = darkMode ? "#ffffff" : "#000000",
            BorderColor = darkMode ? "#4a4b55" : "#e1e4e8",
            KeywordColor = darkMode ? "#569cd6" : "#0000ff",
            TypeColor = darkMode ? "#ff8080" : "#ff0000",
            IdentColor = darkMode ? "#ffffff" : "#000000",
            NameColor = darkMode ? "#ffffff" : "#000000",
            FunctionColor = darkMode ? "#ff8080" : "#ff0000",
            OperatorColor = darkMode ? "#ffffff" :  "#000000",
            NumberColor = darkMode ? "#ffffff" : "#000000",
            StringColor = darkMode ? "#7ec87e" : "#006439",
            CommentColor = darkMode ? "#6aab6a" : "#006439",
            LineNumberColor = darkMode ? "#999999" : "#777777"
        };
    }

    /// <summary>
    /// Get Solarized Dark theme
    /// </summary>
    public static SyntaxHighlightingSettings GetOpenModelicaTheme(bool darkMode)
    {
        return new SyntaxHighlightingSettings
        {
            BackgroundColor = darkMode ? "#32333d" : "#ffffff",
            TextColor = darkMode ? "#ffffff" : "#000000",
            BorderColor = darkMode ? "#4a4b55" : "#e1e4e8",
            KeywordColor = darkMode ? "#e06060" : "#8B0000",
            TypeColor = darkMode ? "#ffffff" : "#000000",
            IdentColor = darkMode ? "#ffffff" : "#000000",
            NameColor = darkMode ? "#ffffff" : "#000000",
            FunctionColor = darkMode ? "#569cd6" : "#0000ff",
            OperatorColor = darkMode ? "#ffffff" : "#000000",
            NumberColor = darkMode ? "#ffffff" : "#000000",
            StringColor = darkMode ? "#7ec87e" : "#006439",
            CommentColor = darkMode ? "#6aab6a" : "#006439",
            LineNumberColor = darkMode ? "#999999" : "#777777"
        };
    }
}

