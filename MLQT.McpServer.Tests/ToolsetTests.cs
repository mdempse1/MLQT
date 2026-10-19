using MLQT.McpServer.Services;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The switch between the current tools and the consolidated set (B503): which names each serves, and
/// how <c>MLQT_MCP_TOOLSET</c> is read.
/// </summary>
public sealed class ToolsetTests
{
    [Fact]
    public void TheConsolidatedSet_IsSixteenTools_AndHoldsEveryFacade()
    {
        Assert.Equal(16, Toolsets.Consolidated.Count);
        Assert.Subset(Toolsets.Consolidated.ToHashSet(StringComparer.Ordinal), Toolsets.ConsolidatedOnly.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryConsolidatedTool_IsATool()
    {
        Assert.Subset(ToolCountTests.DeclaredTools().ToHashSet(StringComparer.Ordinal), Toolsets.Consolidated.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void TheToolsBothSetsHave_AreTheConsolidatedOnesThatAreNotFacades()
    {
        var shared = ToolCountTests.DeclaredTools()
            .Where(n => Toolsets.Includes(McpToolset.Current, n) && Toolsets.Includes(McpToolset.Consolidated, n))
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            ["mlqt_create_library", "mlqt_edit_class", "mlqt_find", "mlqt_format", "mlqt_get_diagram_image",
             "mlqt_get_guidance", "mlqt_list_findings", "mlqt_load", "mlqt_write_class"],
            shared);
    }

    [Theory]
    [InlineData(null, McpToolset.Current)]
    [InlineData("", McpToolset.Current)]
    [InlineData("current", McpToolset.Current)]
    [InlineData(" Consolidated ", McpToolset.Consolidated)]
    [InlineData("consolidated", McpToolset.Consolidated)]
    public void TheEnvironmentVariable_NamesASet(string? value, McpToolset expected)
    {
        Assert.Equal(expected, Toolsets.FromEnvironment(value));
    }

    [Fact]
    public void AnUnknownValue_IsNull_ForTheCallerToReport()
    {
        Assert.Null(Toolsets.FromEnvironment("16"));
    }
}
