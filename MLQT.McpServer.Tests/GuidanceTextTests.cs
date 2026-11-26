using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The guidance and instructions written for the 16 tools (B503): every advertised topic has text, the
/// instructions name one tool to load for each kind of work, and a server sends both.
/// </summary>
public class GuidanceTextTests
{
    [Fact]
    public void EveryTopicHasText_AndEveryTextIsATopic()
    {
        Assert.Equal(GuidanceTools.Topics.Order(StringComparer.Ordinal),
            GuidanceTools.Guidance.Keys.Select(k => k.ToLowerInvariant()).Order(StringComparer.Ordinal));
    }

    public static TheoryData<string> Topics => [.. GuidanceTools.Topics];

    [Theory]
    [MemberData(nameof(Topics))]
    public void EachTopic_IsGivenUnwrapped(string topic)
    {
        var answer = Assert.IsType<GuidanceResult>(new GuidanceTools().GetGuidance(topic));

        Assert.Equal(Prose.Unwrap(GuidanceTools.Guidance[topic]), answer.Guidance);
    }

    /// <summary>
    /// The rules topic lists every rule id there is, each on a line of its own as the agent receives it,
    /// so an id can be looked up rather than guessed - MLQT.Style.NamingConvention for
    /// MLQT.Naming.Convention was a waiver that did nothing. Generated from the catalog, so a rule added
    /// to it is listed without anyone writing guidance; this holds that it stays so.
    /// </summary>
    [Fact]
    public void TheRulesTopic_ListsEveryRuleId_OnALineOfItsOwn()
    {
        var answer = Assert.IsType<GuidanceResult>(new GuidanceTools().GetGuidance("rules"));
        var lines = answer.Guidance.Split('\n');

        Assert.NotEmpty(ModelicaParser.StyleRules.RuleCatalog.BuiltIn);
        Assert.All(ModelicaParser.StyleRules.RuleCatalog.BuiltIn.Values, rule =>
            Assert.Contains(lines, l => l.StartsWith($"- {rule.Id} - {rule.Title}", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.StartsWith("- MLQT.Naming.Convention - ", StringComparison.Ordinal));
        Assert.DoesNotContain("MLQT.Style.NamingConvention", answer.Guidance);
    }

    [Fact]
    public void TheRulesTopic_MarksTheDiagnosticsAsNotWaivable()
    {
        var lines = GuidanceTools.RulesTopic().Split('\n');

        foreach (var id in new[] { ModelicaParser.StyleRules.RuleIds.SyntaxError, ModelicaParser.StyleRules.RuleIds.CheckFailed })
            if (ModelicaParser.StyleRules.RuleCatalog.IsKnown(id))
                Assert.Contains("cannot be waived", lines.Single(l => l.StartsWith($"- {id} - ", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheInstructions_NameATool_ToLoadForEachKindOfWork()
    {
        // The tools to load when deferred, one for each kind of work: the shape that won the step 1 runs.
        Assert.Contains("mlqt_load, mlqt_find, mlqt_get_class, mlqt_edit_class", ServerInstructions.Text);
    }

    [Fact]
    public async Task AServer_SendsTheInstructions_AndGivesTheGuidance()
    {
        await using var server = await InProcessServer.StartAsync();

        Assert.Equal(ServerInstructions.Text, server.Client.ServerInstructions);
        var guidance = MlqtMcpServerExtensionsTests.Text(await server.CallAsync("mlqt_get_guidance", new() { ["topic"] = "editing" }));
        Assert.Contains("mlqt_refactor_class", guidance);
    }
}
