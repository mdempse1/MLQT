using MLQT.McpServer.Dtos;

namespace MLQT.McpServer.Tests;

/// <summary>Helpers for asserting on the <c>object</c> results that tools return
/// (either a success DTO or a <see cref="ToolError"/>).</summary>
internal static class ToolAssert
{
    public static T Ok<T>(object result)
    {
        if (result is ToolError err)
            throw new Xunit.Sdk.XunitException($"Expected {typeof(T).Name} but got ToolError: {err.Error}");
        Assert.IsType<T>(result);
        return (T)result;
    }

    public static ToolError Error(object result)
    {
        Assert.IsType<ToolError>(result);
        return (ToolError)result;
    }
}
