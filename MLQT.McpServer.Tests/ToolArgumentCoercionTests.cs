using System.Text.Json;
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
    public void BuildParameterMap_DiscoversBooleanParameters()
    {
        var map = ToolArgumentCoercion.BuildParameterMap(typeof(EditTools).Assembly);

        Assert.True(map.TryGetValue("create_class", out var createClass));
        Assert.Contains("standalone", createClass!.Booleans);
        Assert.Contains("preview", createClass.Booleans);

        // A tool with no scalar parameters should not appear in the map.
        Assert.True(map.TryGetValue("add_component", out var addComponent));
        Assert.Contains("preview", addComponent!.Booleans);
    }
}
