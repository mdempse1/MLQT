using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MLQT.McpServer.Tests;

/// <summary>
/// What a client reads from this server must not depend on a .NET name, because a consumer ships it
/// obfuscated with every name changed, public ones included (B502, <c>Design/obfuscation.md</c>).
///
/// <para>Each test holds one way a name reaches the wire: a result record's field names, an anonymous
/// object's, an enum written with <c>ToString()</c>, and a tool parameter's name. The first three are
/// made independent of names in the code; the last cannot be, since the SDK reads the parameter's own
/// name, so the tool classes are excluded from renaming instead.</para>
/// </summary>
public sealed class ObfuscationReadinessTests
{
    private static readonly Assembly Server = typeof(ViewTools).Assembly;

    private static IEnumerable<Type> Dtos() => Server.GetTypes()
        .Where(t => t.Namespace == "MLQT.McpServer.Dtos" && t.IsPublic);

    [Fact]
    public void EveryResultRecordIsCompiledInWithReflectionsNames()
    {
        var dtos = Dtos().ToList();
        Assert.True(dtos.Count >= 74, $"only {dtos.Count} result types found; the query is wrong");

        foreach (var dto in dtos)
            JsonContractAssert.MatchesReflection(dto, McpServerJsonContext.Default, McpJsonUtilities.DefaultOptions);
    }

    [Fact]
    public void TheToolsSerializeTheirResultsThroughTheCompiledContracts()
    {
        // The SDK's resolver comes second and reflects, so a record that reached it would still work -
        // with names an obfuscator chose. This is the check that ours are answered first.
        Assert.All(Dtos(), dto =>
            Assert.Same(McpServerJsonContext.Default, McpServerJson.Options.GetTypeInfo(dto).OriginatingResolver));
    }

    [Fact]
    public void EveryToolIsRegisteredWithThoseOptions()
    {
        // Without this, AddMlqtMcpServer could drop the options and every other test here would still
        // pass: reflection produces the same names until an obfuscator runs. The SDK keeps each tool's
        // AIFunction on an internal type, so it is reached by name; if an SDK upgrade moves it, this
        // fails, which is the moment to look at how the options reach the tools again.
        using var provider = new ServiceCollection().AddMlqtMcpServer().Services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>().ToList();
        Assert.Equal(ToolCountTests.DeclaredTools().Count, tools.Count);

        Assert.All(tools, tool =>
        {
            var function = tool.GetType().GetProperty("AIFunction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tool) as AIFunction;
            Assert.NotNull(function);
            Assert.Same(McpServerJson.Options, function.JsonSerializerOptions);
        });
    }

    [Fact]
    public async Task AResultReachesTheClientWithItsFieldNames()
    {
        await using var server = await InProcessServer.StartAsync();

        var result = await server.CallAsync("mlqt_get_guidance", new() { ["topic"] = "views" });
        using var json = JsonDocument.Parse(MlqtMcpServerExtensionsTests.Text(result));

        Assert.Equal("views", json.RootElement.GetProperty("topic").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("guidance").GetString()));
        Assert.Contains("views", json.RootElement.GetProperty("availableTopics").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void NoAnonymousObjectIsBuiltAnywhereInTheServer()
    {
        // An anonymous type's property names are reflected at run time like any other, and the
        // compiler names the type itself, so it cannot be listed in a contract. Every result is a
        // record in Dtos/ instead.
        var offenders = SourceFiles()
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\bnew\s*\{")
                .Select(m => $"{Path.GetFileName(file)} at offset {m.Index}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryToolClassIsExcludedFromRenaming()
    {
        // The SDK builds each tool's input schema, and binds each argument, by the C# parameter's own
        // name, which nothing in code can pin. An obfuscator that honours ObfuscationAttribute reads a
        // plain [Obfuscation] as "exclude from renaming".
        var toolClasses = Server.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null).ToList();
        Assert.Equal(17, toolClasses.Count);

        Assert.All(toolClasses, type =>
        {
            var attribute = type.GetCustomAttribute<ObfuscationAttribute>();
            Assert.True(attribute is { Exclude: true, ApplyToMembers: true, Feature: "all" },
                $"{type.Name} must carry a plain [Obfuscation], which keeps its methods' parameter names");
        });
    }

    [Fact]
    public void TheServersOwnToolsPassTheParameterCheck()
    {
        ToolParameterCheck.Verify(Server);
    }

    [Fact]
    public void RenamedToolParametersStopTheServerSayingWhy()
    {
        // What an obfuscator that renames parameters, with the exclusion attribute ignored, leaves
        // behind: the tools' names intact in their attributes, their parameters not.
        var renamed = typeof(RenamedTools).GetMethods(BindingFlags.Public | BindingFlags.Static);

        var error = Assert.Throws<InvalidOperationException>(() => ToolParameterCheck.Verify(renamed));

        Assert.Contains("'classId'", error.Message);
        Assert.Contains("'a'", error.Message);
        Assert.Contains("declarative protection", error.Message);
    }

    [Fact]
    public void ToolsWhoseAttributesAreGoneStopTheServerToo()
    {
        Assert.Throws<InvalidOperationException>(() => ToolParameterCheck.Verify(Array.Empty<MethodInfo>()));
    }

    public static class RenamedTools
    {
        [McpServerTool(Name = "mlqt_get_class_info")] public static string A(string a) => a;
        [McpServerTool(Name = "mlqt_load_library")] public static string B(string path) => path;
        [McpServerTool(Name = "mlqt_check_style")] public static string C(string source, string settings) => source + settings;
    }

    [Fact]
    public void EveryEnumAToolWritesAsTextKeepsItsMembersName()
    {
        var mappings = typeof(WireNames).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(WireNames.Of))
            .ToList();
        Assert.Equal(8, mappings.Count);

        foreach (var mapping in mappings)
        {
            var enumType = mapping.GetParameters().Single().ParameterType;
            foreach (var value in Enum.GetValues(enumType))
                Assert.Equal(value.ToString(), mapping.Invoke(null, [value]));
        }
    }

    /// <summary>
    /// Every <c>.ToString(</c> left in the server, with what it formats. An enum's <c>ToString()</c>
    /// sends its member's name, which an obfuscated build renames; it goes through
    /// <see cref="WireNames"/>. So a new call has to be added here, saying it is not an enum.
    /// </summary>
    private static readonly (string File, string Fragment, string Why)[] ToStringLedger =
    [
        ("ConnectionLineAnnotator.cs", "Math.Round(v).ToString(CultureInfo.InvariantCulture)", "a coordinate"),
        ("Prose.cs", "return result.ToString();", "a StringBuilder"),
        ("WireNames.cs", "Convert.ToInt64(value", "the number of an unnamed enum value, deliberately"),
        ("ToolUsageLogger.cs", "DateTime.UtcNow.ToString(\"o\")", "a timestamp"),
        ("ToolUsageLogger.cs", "isError.ToString()", "a bool"),
        ("EditTools.cs", "return builder.ToString();", "a StringBuilder"),
        ("EditTools.cs", "return builder.ToString();", "a StringBuilder"),
    ];

    [Fact]
    public void EveryToStringIsOnTheLedger()
    {
        var calls = SourceFiles()
            .SelectMany(file => File.ReadLines(file)
                .Where(line => line.Contains(".ToString(", StringComparison.Ordinal))
                .Select(line => (File: Path.GetFileName(file), Line: line.Trim())))
            .ToList();

        var unaccounted = calls
            .Where(call => !ToStringLedger.Any(entry => entry.File == call.File && call.Line.Contains(entry.Fragment, StringComparison.Ordinal)))
            .Select(call => $"{call.File}: {call.Line}")
            .ToList();

        Assert.Empty(unaccounted);
        Assert.Equal(ToStringLedger.Length, calls.Count);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = Path.Combine(ToolCountTests.RepositoryRoot(), "MLQT.McpServer");
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}") && !f.Contains($"{separator}bin{separator}"));
    }
}
