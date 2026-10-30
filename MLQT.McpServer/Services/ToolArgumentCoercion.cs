using System.Runtime.CompilerServices;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace MLQT.McpServer.Services;

/// <summary>
/// Makes tool-argument binding tolerant of scalars that LLM clients commonly send as JSON strings — a
/// boolean as <c>"true"</c>, a number as <c>"5"</c>. The MCP SDK deserializes each argument strictly by
/// its declared parameter type, so a quoted boolean throws an opaque "An error occurred invoking
/// '&lt;tool&gt;'" before the tool method runs. This coerces such strings to the JSON type the parameter
/// expects, so it never touches a parameter that is genuinely a string. Done in the request filter
/// (not via a JSON converter) so each tool's input schema keeps its precise <c>"type"</c>.
///
/// <para>A list of strings is the same mistake one level up: <c>classIds: "P.A"</c> for one class, or
/// the list sent as the text of a JSON array. A bare string becomes a list of one, and a string that
/// parses as an array of strings becomes that array.</para>
/// </summary>
/// <remarks>
/// <b>Guided by each tool's input schema, not by reflecting over this assembly.</b> A server built on
/// <see cref="MlqtMcpServerExtensions.AddMlqtMcpServer"/> registers tools of its own beside these, and
/// the schema is the one description every registered tool has, so the host's tools are coerced as
/// MLQT's are with nothing copied into it. The schema is what a client is told the parameter is, which
/// is exactly what a quoted scalar has to be turned into.
/// </remarks>
internal static class ToolArgumentCoercion
{
    /// <summary>The boolean-, number- and string-list-typed parameter names of a single tool.</summary>
    internal sealed record ScalarParameters(HashSet<string> Booleans, HashSet<string> Numbers, HashSet<string> StringLists)
    {
        public bool IsEmpty => Booleans.Count == 0 && Numbers.Count == 0 && StringLists.Count == 0;
    }

    // A tool's schema does not change once it is built, so it is read once per tool instance; keyed
    // weakly, so a tool a host removes from the collection takes its entry with it.
    private static readonly ConditionalWeakTable<McpServerTool, ScalarParameters> PerTool = new();

    /// <summary>The scalar parameters of <paramref name="tool"/>, read from its input schema once.</summary>
    public static ScalarParameters For(McpServerTool tool) =>
        PerTool.GetValue(tool, t => ForSchema(t.ProtocolTool.InputSchema));

    /// <summary>
    /// Which top-level properties of a tool's input schema are boolean, and which are integer or
    /// number. A nullable parameter is advertised as <c>["boolean", "null"]</c>, so a type array counts
    /// when it names the scalar.
    /// </summary>
    public static ScalarParameters ForSchema(JsonElement schema)
    {
        var booleans = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        var stringLists = new HashSet<string>(StringComparer.Ordinal);

        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object ||
                    !property.Value.TryGetProperty("type", out var type))
                    continue;

                if (Names(type, "boolean"))
                    booleans.Add(property.Name);
                else if (Names(type, "integer") || Names(type, "number"))
                    numbers.Add(property.Name);
                else if (Names(type, "array") && IsStringItems(property.Value))
                    stringLists.Add(property.Name);
            }
        }

        return new ScalarParameters(booleans, numbers, stringLists);
    }

    private static bool IsStringItems(JsonElement property) =>
        property.TryGetProperty("items", out var items) &&
        items.ValueKind == JsonValueKind.Object &&
        items.TryGetProperty("type", out var itemType) &&
        Names(itemType, "string");

    private static bool Names(JsonElement type, string name) => type.ValueKind switch
    {
        JsonValueKind.String => type.ValueEquals(name),
        JsonValueKind.Array => type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.ValueEquals(name)),
        _ => false,
    };

    /// <summary>
    /// Return an argument dictionary with any string-encoded boolean/number values (for the parameters
    /// named in <paramref name="scalars"/>) replaced by the corresponding JSON scalar. The input is
    /// returned unchanged when nothing needed coercing, so the common case allocates nothing.
    /// </summary>
    public static IDictionary<string, JsonElement> Coerce(
        IDictionary<string, JsonElement> arguments, ScalarParameters scalars)
    {
        Dictionary<string, JsonElement>? coerced = null;

        foreach (var (key, value) in arguments)
        {
            if (value.ValueKind != JsonValueKind.String)
                continue;

            JsonElement? replacement = scalars.Booleans.Contains(key)
                ? TryParseBoolean(value.GetString())
                : scalars.Numbers.Contains(key)
                    ? TryParseNumber(value.GetString())
                    : scalars.StringLists.Contains(key)
                        ? ToStringList(value.GetString())
                        : null;

            if (replacement is { } rep)
            {
                coerced ??= new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
                coerced[key] = rep;
            }
        }

        return coerced ?? arguments;
    }

    private static JsonElement? TryParseBoolean(string? text)
    {
        if (bool.TryParse(text, out var b))
            return JsonSerializer.SerializeToElement(b);
        if (text == "1") return JsonSerializer.SerializeToElement(true);
        if (text == "0") return JsonSerializer.SerializeToElement(false);
        return null; // leave it for the SDK to reject with its own error
    }

    /// <summary>The text of a JSON array of strings as that array, and any other text as a list of one.</summary>
    private static JsonElement? ToStringList(string? text)
    {
        if (text is null)
            return null;

        if (text.TrimStart().StartsWith('['))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(text);
                if (parsed is not null)
                    return JsonSerializer.SerializeToElement(parsed);
            }
            catch (JsonException)
            {
                // Not an array after all: one item, like any other text.
            }
        }

        return JsonSerializer.SerializeToElement(new[] { text });
    }

    private static JsonElement? TryParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (long.TryParse(text, out var l))
            return JsonSerializer.SerializeToElement(l);
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return JsonSerializer.SerializeToElement(d);
        return null;
    }
}
