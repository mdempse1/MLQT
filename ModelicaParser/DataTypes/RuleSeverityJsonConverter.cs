using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelicaParser.DataTypes;

/// <summary>
/// Writes a <see cref="RuleSeverity"/> as its name and reads it back, without asking the enum about
/// itself (B502).
/// </summary>
/// <remarks>
/// <para>It replaces <c>JsonStringEnumConverter</c>, which builds its table of names by reflecting over
/// the enum's members. An obfuscator may rename those members or delete them from metadata, and then
/// that converter writes numbers - into every committed <c>.mlqt/settings.json</c> and baseline. The
/// names come from <see cref="RuleSeverityText"/> instead.</para>
/// <para>Public because the JSON source generator in another assembly has to name it.</para>
/// <para>It reads what the reflection converter read: a name in any case and with surrounding spaces,
/// a number, a number in a string, and a comma-separated list, combined as flags. Nobody writes the
/// last, but a file that has it still loads as it did.</para>
/// </remarks>
public sealed class RuleSeverityJsonConverter : JsonConverter<RuleSeverity>
{
    public override RuleSeverity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            return (RuleSeverity)number;

        if (reader.TokenType == JsonTokenType.String && Parse(reader.GetString()) is { } severity)
            return severity;

        throw new JsonException($"'{Describe(ref reader)}' is not a rule severity: Off, Info, Warning or Error.");
    }

    public override void Write(Utf8JsonWriter writer, RuleSeverity value, JsonSerializerOptions options)
    {
        var text = RuleSeverityText.Of(value);
        if (RuleSeverityText.TryParse(text, out _))
            writer.WriteStringValue(text);
        else
            writer.WriteNumberValue((int)value);
    }

    private static RuleSeverity? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var combined = 0;
        foreach (var part in text.Split(','))
        {
            var trimmed = part.Trim();
            if (RuleSeverityText.TryParse(trimmed, out var named))
                combined |= (int)named;
            else if (int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                combined |= number;
            else
                return null;
        }
        return (RuleSeverity)combined;
    }

    private static string Describe(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString() ?? string.Empty,
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        JsonTokenType.Null => "null",
        _ => reader.TokenType.ToString(),
    };
}
