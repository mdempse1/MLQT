using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace MLQT.McpServer.Services;

/// <summary>
/// Makes tool-argument binding tolerant of scalars that LLM clients commonly send as JSON strings — a
/// boolean as <c>"true"</c>, a number as <c>"5"</c>. The MCP SDK deserializes each argument strictly by
/// its declared parameter type, so a quoted boolean throws an opaque "An error occurred invoking
/// '&lt;tool&gt;'" before the tool method runs. This coerces such strings to the JSON type the parameter
/// expects, guided by the tool method signatures, so it never touches a parameter that is genuinely a
/// string. Done in the request filter (not via a JSON converter) so each tool's input schema keeps its
/// precise <c>"type"</c>.
/// </summary>
internal static class ToolArgumentCoercion
{
    /// <summary>The boolean- and number-typed parameter names of a single tool.</summary>
    internal sealed record ScalarParameters(HashSet<string> Booleans, HashSet<string> Numbers);

    private static readonly HashSet<Type> NumericTypes = new()
    {
        typeof(int), typeof(long), typeof(short), typeof(byte), typeof(sbyte),
        typeof(uint), typeof(ulong), typeof(ushort), typeof(double), typeof(float), typeof(decimal),
    };

    /// <summary>
    /// Reflect over every [McpServerTool] method in the assembly and record which of each tool's
    /// parameters are boolean or numeric (unwrapping Nullable&lt;T&gt;). Tools with no such parameter are
    /// omitted, so the filter can skip coercion entirely for them.
    /// </summary>
    public static Dictionary<string, ScalarParameters> BuildParameterMap(Assembly assembly)
    {
        var map = new Dictionary<string, ScalarParameters>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr is null)
                    continue;

                var toolName = string.IsNullOrEmpty(toolAttr.Name) ? method.Name : toolAttr.Name;
                var booleans = new HashSet<string>(StringComparer.Ordinal);
                var numbers = new HashSet<string>(StringComparer.Ordinal);

                foreach (var p in method.GetParameters())
                {
                    if (p.Name is null)
                        continue;
                    var t = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
                    if (t == typeof(bool))
                        booleans.Add(p.Name);
                    else if (NumericTypes.Contains(t))
                        numbers.Add(p.Name);
                }

                if (booleans.Count > 0 || numbers.Count > 0)
                    map[toolName] = new ScalarParameters(booleans, numbers);
            }
        }

        return map;
    }

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
