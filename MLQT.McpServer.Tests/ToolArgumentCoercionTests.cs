using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class ToolArgumentCoercionTests
{
    private static JsonElement Str(string s) => JsonSerializer.SerializeToElement(s);

    private static ToolArgumentCoercion.ScalarParameters Scalars(string[] bools, string[] nums)
        => new(new HashSet<string>(bools), new HashSet<string>(nums));

    [Fact]
    public void Coerce_StringEncodedBoolean_BecomesJsonBoolean()
    {
        var args = new Dictionary<string, JsonElement> { ["standalone"] = Str("true"), ["preview"] = Str("false") };
        var result = ToolArgumentCoercion.Coerce(args, Scalars(new[] { "standalone", "preview" }, Array.Empty<string>()));

        Assert.Equal(JsonValueKind.True, result["standalone"].ValueKind);
        Assert.Equal(JsonValueKind.False, result["preview"].ValueKind);
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("FALSE", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void Coerce_AcceptsCommonBooleanSpellings(string text, bool expected)
    {
        var args = new Dictionary<string, JsonElement> { ["flag"] = Str(text) };
        var result = ToolArgumentCoercion.Coerce(args, Scalars(new[] { "flag" }, Array.Empty<string>()));
        Assert.Equal(expected, result["flag"].GetBoolean());
    }

    [Fact]
    public void Coerce_StringEncodedNumber_BecomesJsonNumber()
    {
        var args = new Dictionary<string, JsonElement> { ["count"] = Str("5") };
        var result = ToolArgumentCoercion.Coerce(args, Scalars(Array.Empty<string>(), new[] { "count" }));
        Assert.Equal(JsonValueKind.Number, result["count"].ValueKind);
        Assert.Equal(5, result["count"].GetInt32());
    }

    [Fact]
    public void Coerce_LeavesGenuineStringParameterAlone()
    {
        // 'description' is not a scalar parameter, so the string "true" stays a string.
        var args = new Dictionary<string, JsonElement> { ["description"] = Str("true") };
        var result = ToolArgumentCoercion.Coerce(args, Scalars(new[] { "standalone" }, Array.Empty<string>()));

        Assert.Same(args, result); // nothing changed -> same instance, no allocation
        Assert.Equal(JsonValueKind.String, result["description"].ValueKind);
    }

    [Fact]
    public void Coerce_AlreadyTypedValue_Unchanged()
    {
        var args = new Dictionary<string, JsonElement> { ["standalone"] = JsonSerializer.SerializeToElement(true) };
        var result = ToolArgumentCoercion.Coerce(args, Scalars(new[] { "standalone" }, Array.Empty<string>()));
        Assert.Same(args, result);
        Assert.Equal(JsonValueKind.True, result["standalone"].ValueKind);
    }

    [Fact]
    public void Coerce_UnparseableBoolean_LeftForServerToReject()
    {
        var args = new Dictionary<string, JsonElement> { ["standalone"] = Str("maybe") };
        var result = ToolArgumentCoercion.Coerce(args, Scalars(new[] { "standalone" }, Array.Empty<string>()));
        Assert.Equal(JsonValueKind.String, result["standalone"].ValueKind); // untouched
    }

    [Fact]
    public void ForSchema_ReadsBooleansAndNumbers()
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "flag":{"type":"boolean","default":false},
              "maybe":{"type":["boolean","null"]},
              "count":{"type":"integer"},
              "scale":{"type":["number","null"]},
              "name":{"type":"string"},
              "untyped":{}
            }}
            """).RootElement;

        var scalars = ToolArgumentCoercion.ForSchema(schema);

        Assert.Equal(["flag", "maybe"], scalars.Booleans.Order(StringComparer.Ordinal));
        Assert.Equal(["count", "scale"], scalars.Numbers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ForSchema_WithoutPropertiesIsEmpty()
    {
        Assert.True(ToolArgumentCoercion.ForSchema(JsonDocument.Parse("""{"type":"object"}""").RootElement).IsEmpty);
        Assert.True(ToolArgumentCoercion.ForSchema(JsonDocument.Parse("true").RootElement).IsEmpty);
    }

    /// <summary>
    /// The schema is what the coercion reads now, where it once reflected over the tool methods. For
    /// every MLQT tool the two must name the same parameters, or moving to the schema lost a coercion -
    /// a quoted scalar the server used to accept and now refuses with an opaque error.
    /// </summary>
    [Fact]
    public void ForSchema_AgreesWithEveryToolsSignature()
    {
        using var provider = new ServiceCollection().AddMlqtMcpServer().Services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name, StringComparer.Ordinal);
        var methods = typeof(EditTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => (Method: m, Name: m.GetCustomAttribute<McpServerToolAttribute>()?.Name))
            .Where(m => m.Name is not null)
            .ToList();

        Assert.Equal(methods.Count, tools.Count);
        Assert.Contains(methods, m => m.Name == "mlqt_create_library"); // the query found the tools

        foreach (var (method, name) in methods)
        {
            var scalars = ToolArgumentCoercion.For(tools[name!]);
            var parameters = method.GetParameters()
                .Select(p => (p.Name!, Type: Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType))
                .ToList();

            Assert.Equal(
                parameters.Where(p => p.Type == typeof(bool)).Select(p => p.Item1).Order(StringComparer.Ordinal),
                scalars.Booleans.Order(StringComparer.Ordinal));
            Assert.Equal(
                parameters.Where(p => NumericTypes.Contains(p.Type)).Select(p => p.Item1).Order(StringComparer.Ordinal),
                scalars.Numbers.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void For_ReadsEachToolOnce()
    {
        using var provider = new ServiceCollection().AddMlqtMcpServer().Services.BuildServiceProvider();
        var tool = provider.GetServices<McpServerTool>().First(t => t.ProtocolTool.Name == "mlqt_create_library");

        Assert.Same(ToolArgumentCoercion.For(tool), ToolArgumentCoercion.For(tool));
        Assert.Contains("loadIntoSession", ToolArgumentCoercion.For(tool).Booleans);
    }

    private static readonly HashSet<Type> NumericTypes =
    [
        typeof(int), typeof(long), typeof(short), typeof(byte), typeof(sbyte),
        typeof(uint), typeof(ulong), typeof(ushort), typeof(double), typeof(float), typeof(decimal),
    ];
}
