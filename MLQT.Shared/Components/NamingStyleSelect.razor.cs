using ModelicaParser.StyleRules;
using System.Text.RegularExpressions;

namespace MLQT.Shared.Components;

public partial class NamingStyleSelect
{
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public NamingStyle Value { get; set; }
    [Parameter] public EventCallback<NamingStyle> ValueChanged { get; set; }
    [Parameter] public string SlotKey { get; set; } = "";
    [Parameter] public List<string>? Patterns { get; set; }
    [Parameter] public EventCallback<List<string>> PatternsChanged { get; set; }

    private bool _showPatterns;
    private string _newPattern = "";
    private string? _patternError;

    private int PatternCount => Patterns?.Count ?? 0;

    private async Task OnValueChanged(NamingStyle newValue)
    {
        Value = newValue;
        await ValueChanged.InvokeAsync(newValue);
    }

    /// <summary>
    /// Turns what the user typed into an exception pattern, or into the reason it is not one.
    /// </summary>
    /// <returns>
    /// The pattern to add, and the error to show. Both null means there was nothing to add — an
    /// empty box is not a mistake, so it is neither accepted nor complained about.
    /// </returns>
    /// <remarks>
    /// A rejected pattern matters more than it looks: an exception name that never compiles would
    /// be stored, reloaded on every check, and throw somewhere far from the settings page that
    /// accepted it. The sanitising step exists because the settings UI shows patterns wrapped in
    /// brackets and users paste them back in that form.
    /// </remarks>
    internal static (string? Pattern, string? Error) ParsePattern(string? input)
    {
        var pattern = input?.Trim();
        if (string.IsNullOrEmpty(pattern))
            return (null, null);

        // Sanitize accidental bracket-wrapping (e.g., "[^[A-Z]...$]" → "^[A-Z]...$")
        pattern = NamingValidator.SanitizePattern(pattern);

        try
        {
            _ = new Regex(pattern);
        }
        catch (RegexParseException ex)
        {
            return (null, $"Invalid regex: {ex.Message}");
        }

        return (pattern, null);
    }

    private async Task AddPattern()
    {
        var (pattern, error) = ParsePattern(_newPattern);
        _patternError = error;
        if (pattern is null)
            return;

        if (Patterns != null && !Patterns.Contains(pattern))
        {
            Patterns.Add(pattern);
            _newPattern = "";
            await PatternsChanged.InvokeAsync(Patterns);
            _showPatterns = false;
        }
    }

    private async Task RemovePattern(string pattern)
    {
        Patterns?.Remove(pattern);
        await PatternsChanged.InvokeAsync(Patterns!);
    }
}
