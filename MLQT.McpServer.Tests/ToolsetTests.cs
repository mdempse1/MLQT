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

    // What each consolidated tool does to its world, from the plan's Writes column: a client may run a
    // read-only one without asking, and asks before a destructive one. OpenWorld is false for all: each
    // works on the local files and session only.
    [Theory]
    [InlineData("mlqt_load", true, false, true)]
    [InlineData("mlqt_session", true, false, false)]
    [InlineData("mlqt_create_library", false, false, false)]
    [InlineData("mlqt_find", true, false, true)]
    [InlineData("mlqt_browse", true, false, true)]
    [InlineData("mlqt_get_class", true, false, true)]
    [InlineData("mlqt_get_diagram_image", true, false, true)]
    [InlineData("mlqt_write_class", false, true, false)]
    [InlineData("mlqt_edit_class", false, true, false)]
    [InlineData("mlqt_refactor_class", false, true, false)]
    [InlineData("mlqt_format", false, false, true)]
    [InlineData("mlqt_check", true, false, true)]
    [InlineData("mlqt_list_findings", true, false, true)]
    [InlineData("mlqt_style_settings", false, false, true)]
    [InlineData("mlqt_dependencies", true, false, true)]
    [InlineData("mlqt_get_guidance", true, false, true)]
    public async Task EachConsolidatedTool_SaysWhatItChanges(string name, bool readOnly, bool destructive, bool idempotent)
    {
        await using var server = await InProcessServer.StartAsync(new MlqtMcpServerOptions { Toolset = McpToolset.Consolidated });
        var tool = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Single(t => t.Name == name);

        var hints = tool.ProtocolTool.Annotations!;
        Assert.Equal(readOnly, hints.ReadOnlyHint);
        Assert.False(hints.OpenWorldHint);
        if (!readOnly)   // the other two mean something only for a tool that changes things
            Assert.Equal((destructive, idempotent), (hints.DestructiveHint, hints.IdempotentHint));
    }

    [Fact]
    public void EveryConsolidatedTool_HasAHintCase()
    {
        var cases = typeof(ToolsetTests).GetMethod(nameof(EachConsolidatedTool_SaysWhatItChanges))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false).Cast<InlineDataAttribute>()
            .Select(d => (string)d.Data[0]!);

        Assert.Equal(Toolsets.Consolidated.Order(StringComparer.Ordinal), cases.Order(StringComparer.Ordinal));
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
