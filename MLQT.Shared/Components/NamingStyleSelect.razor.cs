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

    private async Task AddPattern()
    {
        _patternError = null;
        var pattern = _newPattern.Trim();
        if (string.IsNullOrEmpty(pattern))
            return;

        // Sanitize accidental bracket-wrapping (e.g., "[^[A-Z]...$]" → "^[A-Z]...$")
        pattern = NamingValidator.SanitizePattern(pattern);

        try
        {
            _ = new Regex(pattern);
        }
        catch (RegexParseException ex)
        {
            _patternError = $"Invalid regex: {ex.Message}";
            return;
        }

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
