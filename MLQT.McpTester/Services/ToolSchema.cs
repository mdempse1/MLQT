using System.Text.Json;
using System.Text.Json.Nodes;

namespace MLQT.McpTester.Services;

/// <summary>A single input parameter derived from a tool's JSON Schema, plus its edited value.</summary>
public sealed class ToolParam
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "string";
    public string? Description { get; init; }
    public bool Required { get; init; }
    public IReadOnlyList<string>? EnumValues { get; init; }

    // Bound values (string for everything except booleans).
    public string StringValue { get; set; } = "";

    // Boolean value: null means "unset" so the parameter is omitted and the server applies its own
    // default. This matters for tri-state bool? parameters (e.g. create_class's 'standalone', where
    // unset = auto-choose, true = force standalone, false = force nested) — always sending false would
    // silently override the server's automatic choice.
    public bool? BoolValue { get; set; }

    /// <summary>Three-way binding for the boolean selector: "" (unset/default), "true" or "false".</summary>
    public string BoolChoice
    {
        get => BoolValue is null ? "" : (BoolValue.Value ? "true" : "false");
        set => BoolValue = value switch { "true" => true, "false" => false, _ => null };
    }

    public bool IsBoolean => Type == "boolean";
    public bool IsEnum => EnumValues is { Count: > 0 };
    public bool IsMultiline => Type is "array" or "object";
}

/// <summary>Parses MCP tool input schemas into editable parameters and converts them back to a
/// typed argument dictionary for a tool call.</summary>
public static class ToolSchema
{
    public static List<ToolParam> Parse(JsonElement schema)
    {
        var result = new List<ToolParam>();
        if (schema.ValueKind != JsonValueKind.Object)
            return result;

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            foreach (var r in req.EnumerateArray())
                if (r.GetString() is { } s) required.Add(s);

        if (!schema.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var p in props.EnumerateObject())
        {
            var ps = p.Value;
            List<string>? enumValues = null;
            if (ps.ValueKind == JsonValueKind.Object && ps.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array)
                enumValues = en.EnumerateArray().Select(e => e.ToString()).ToList();

            var param = new ToolParam
            {
                Name = p.Name,
                Type = ExtractType(ps),
                Description = ps.ValueKind == JsonValueKind.Object && ps.TryGetProperty("description", out var d)
                    ? d.GetString() : null,
                Required = required.Contains(p.Name),
                EnumValues = enumValues,
            };

            // Pre-fill from a schema default, if present.
            if (ps.ValueKind == JsonValueKind.Object && ps.TryGetProperty("default", out var def))
            {
                if (param.Type == "boolean" && def.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    param.BoolValue = def.GetBoolean();
                else if (def.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    param.StringValue = def.ToString();
            }

            result.Add(param);
        }

        return result;
    }

    /// <summary>Builds the argument dictionary for a tool call. Optional parameters left blank are
    /// omitted so the server applies its own defaults. Throws <see cref="FormatException"/> with a
    /// friendly message if a value cannot be converted to its declared type.</summary>
    public static Dictionary<string, object?> BuildArguments(IEnumerable<ToolParam> parameters)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var p in parameters)
        {
            if (p.IsBoolean)
            {
                if (p.BoolValue is bool b)
                    args[p.Name] = b;
                else if (p.Required)
                    throw new FormatException($"'{p.Name}' is required.");
                // optional & unset -> omit so the server applies its own default
                continue;
            }

            var raw = p.StringValue?.Trim() ?? "";
            if (raw.Length == 0)
            {
                if (p.Required)
                    throw new FormatException($"'{p.Name}' is required.");
                continue; // optional & blank -> let the server default it
            }

            try
            {
                args[p.Name] = p.Type switch
                {
                    "integer" => long.Parse(raw),
                    "number" => double.Parse(raw),
                    "array" or "object" => JsonNode.Parse(raw),
                    _ => raw,
                };
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or JsonException)
            {
                var hint = p.Type is "array" or "object" ? "valid JSON" : $"a valid {p.Type}";
                throw new FormatException($"'{p.Name}' must be {hint}.");
            }
        }
        return args;
    }

    private static string ExtractType(JsonElement ps)
    {
        if (ps.ValueKind != JsonValueKind.Object)
            return "string";

        if (ps.TryGetProperty("type", out var t))
        {
            if (t.ValueKind == JsonValueKind.String)
                return t.GetString() ?? "string";
            if (t.ValueKind == JsonValueKind.Array)
                foreach (var item in t.EnumerateArray())
                    if (item.GetString() is { } s && s != "null")
                        return s;
        }
        return "string";
    }
}
