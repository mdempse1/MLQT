using System.Text.RegularExpressions;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The consolidated set's guidance and instructions (B503 step 2): the same topics as the current set's,
/// naming only tools the consolidated set serves, and sent by a server serving it.
/// </summary>
public class ConsolidatedGuidanceTests
{
    private static readonly Regex ToolName = new(@"\bmlqt_[a-z_]+", RegexOptions.CultureInvariant);

    [Fact]
    public void ItHasTheSameTopicsAsTheCurrentSet()
    {
        Assert.Equal(GuidanceTools.Topics.Order(StringComparer.Ordinal),
            ConsolidatedGuidance.Guidance.Keys.Select(k => k.ToLowerInvariant()).Order(StringComparer.Ordinal));
    }

    public static TheoryData<string> Topics => [.. GuidanceTools.Topics];

    [Theory]
    [MemberData(nameof(Topics))]
    public void EachTopic_NamesOnlyToolsTheConsolidatedSetServes(string topic)
    {
        var named = ToolName.Matches(ConsolidatedGuidance.Guidance[topic]).Select(m => m.Value).Distinct();

        Assert.All(named, name => Assert.Contains(name, Toolsets.Consolidated));
    }

    [Fact]
    public void TheInstructions_NameOnlyToolsTheConsolidatedSetServes()
    {
        var named = ToolName.Matches(ServerInstructions.ConsolidatedText).Select(m => m.Value).Distinct().ToList();

        Assert.All(named, name => Assert.Contains(name, Toolsets.Consolidated));
        // The tools to load when deferred, one for each kind of work: the shape that won the step 1 runs.
        Assert.Contains("mlqt_load, mlqt_find, mlqt_get_class, mlqt_edit_class", ServerInstructions.ConsolidatedText);
    }

    [Theory]
    [MemberData(nameof(Topics))]
    public void EachSet_GetsItsOwnTopic(string topic)
    {
        var current = Assert.IsType<GuidanceResult>(new GuidanceTools(McpToolset.Current).GetGuidance(topic));
        var consolidated = Assert.IsType<GuidanceResult>(new GuidanceTools(McpToolset.Consolidated).GetGuidance(topic));

        Assert.Equal(Prose.Unwrap(GuidanceTools.Guidance[topic]), current.Guidance);
        Assert.Equal(Prose.Unwrap(ConsolidatedGuidance.Guidance[topic]), consolidated.Guidance);
    }

    [Fact]
    public async Task AConsolidatedServer_SendsItsInstructions_AndGivesItsGuidance()
    {
        await using var server = await InProcessServer.StartAsync(new MlqtMcpServerOptions { Toolset = McpToolset.Consolidated });

        Assert.Equal(ServerInstructions.ConsolidatedText, server.Client.ServerInstructions);
        var guidance = MlqtMcpServerExtensionsTests.Text(await server.CallAsync("mlqt_get_guidance", new() { ["topic"] = "editing" }));
        Assert.Contains("mlqt_refactor_class", guidance);
        Assert.DoesNotContain("mlqt_update_class_source", guidance);
    }

    [Fact]
    public async Task ACurrentServer_StillGivesTheCurrentGuidance()
    {
        await using var server = await InProcessServer.StartAsync();

        var guidance = MlqtMcpServerExtensionsTests.Text(await server.CallAsync("mlqt_get_guidance", new() { ["topic"] = "editing" }));
        Assert.Contains("mlqt_update_class_source", guidance);
    }
}
