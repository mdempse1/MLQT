using System.Text.Json;
using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Dymola's JSON-RPC envelope is written with literal names rather than as an anonymous object, whose
/// names an obfuscated build renames (B502). These hold it to the bytes the anonymous object produced,
/// for the shapes the interface sends.
/// </summary>
public class JsonRpcRequestTests
{
    public static TheoryData<string, object?[]?, int> Requests => new()
    {
        { "ping", null, 1 },
        { "simulateModel", ["Lib.Model", 0.0, 1.5, 500, 0, "Dassl", 1e-4, 0.0, "result"], 7 },
        { "plot", [new object?[] { "a.y", "b \"quoted\" \\ é" }], 12 },
        { "setClassText", ["Parent", "class foo end foo;"], 3 },
        { "translateModel", [], 4 },
        { "f", [true, false, null, new object?[] { new object?[] { 1, 2 }, new object?[] { 3.25 } }], int.MaxValue },
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public void IsWhatTheAnonymousRequestWrote(string method, object?[]? parameters, int id)
    {
        var anonymous = JsonSerializer.Serialize(new { method, @params = parameters, id });

        Assert.Equal(anonymous, DymolaInterface.JsonRpcRequest(method, parameters, id));
    }
}
