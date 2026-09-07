using MudBlazor.Utilities;

namespace MLQT.Shared.Components;

public partial class ColorPicker
{
    [Parameter]
    public string Value { get; set; } = "#000000";

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    [Parameter]
    public string Label { get; set; } = "Color";

    [Parameter]
    public string Class { get; set; } = "";

    private MudColor _mudColor = "#000000";

    protected override void OnParametersSet()
    {
        if (IsValidHexColor(Value))
        {
            MudColor incoming = Value;
            if (incoming != _mudColor)
                _mudColor = incoming;
        }
    }

    private async Task OnMudColorChanged(MudColor color)
    {
        _mudColor = color;
        var hex = $"#{color.R:x2}{color.G:x2}{color.B:x2}";
        await ValueChanged.InvokeAsync(hex);
    }

    private static bool IsValidHexColor(string? value)
    {
        if (value == null || value.Length != 7 || value[0] != '#')
            return false;
        foreach (var c in value.AsSpan(1))
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }
}
