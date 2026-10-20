using System.Reflection;
using ModelContextProtocol.Server;

namespace MLQT.McpServer.Services;

/// <summary>
/// Refuses to start a server whose tool parameters have been renamed (B502).
/// </summary>
/// <remarks>
/// <para>The SDK builds each tool's input schema, and binds each argument, by the C# parameter's own
/// name. An obfuscator that renames parameters therefore breaks every call - and silently: the server
/// starts, lists its tools, and binds nothing a client sends. The tool classes carry
/// <c>[Obfuscation]</c> to exclude them, but whether that is honoured is the obfuscator's setting, not
/// MLQT's (at least one ignores it unless a declarative-protection option is on, and says nothing).</para>
/// <para>So the server checks a few parameters it knows, at registration, and fails with a message
/// that says what happened and what to change. Renaming is all or nothing, so a few are enough.</para>
/// </remarks>
internal static class ToolParameterCheck
{
    // Tools and the parameter names clients send them, written here as text so that renaming cannot
    // change both sides of the comparison.
    private static readonly (string Tool, string[] Parameters)[] Known =
    [
        ("mlqt_get_class", ["classIds"]),
        ("mlqt_load", ["paths"]),
        ("mlqt_check", ["source", "settings"]),
    ];

    public static void Verify(Assembly toolAssembly) => Verify(ToolMethods(toolAssembly));

    public static void Verify(IEnumerable<MethodInfo> toolMethods)
    {
        var byTool = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        foreach (var method in toolMethods)
            if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is { } name)
                byTool[name] = method;

        foreach (var (tool, parameters) in Known)
        {
            if (!byTool.TryGetValue(tool, out var method))
                throw new InvalidOperationException(
                    $"MLQT's MCP tool '{tool}' was not found among the tool classes. If this build is " +
                    "obfuscated, the tool classes and their attributes must be excluded from it.");

            var actual = method.GetParameters().Select(p => p.Name ?? string.Empty).ToList();
            var missing = parameters.Where(p => !actual.Contains(p, StringComparer.Ordinal)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"MLQT's MCP tool '{tool}' expects a parameter named '{missing[0]}' but its method's " +
                    $"parameters are named {string.Join(", ", actual.Select(a => $"'{a}'"))}. The parameters " +
                    "have been renamed - by an obfuscator, if this build is protected - so no client's " +
                    "arguments would bind. The tool classes carry [Obfuscation] to exclude them from " +
                    "renaming: turn on the obfuscator's option that honours that attribute (often called " +
                    "declarative protection), or exclude the MLQT.McpServer.Tools namespace another way.");
        }
    }

    private static IEnumerable<MethodInfo> ToolMethods(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
}
