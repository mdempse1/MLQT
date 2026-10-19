using MLQT.McpServer.Services;

namespace MLQT.McpServer.Tests;

/// <summary>
/// While the consolidated set is served, an answer names only its tools (B503): the facades call the
/// current tools, whose errors and notes name current ones, and the step-3 runs found an agent told to
/// "find it with mlqt_search_classes", a tool that set does not have.
/// </summary>
public sealed class ConsolidatedNamesTests
{
    [Fact]
    public void EveryCurrentOnlyTool_IsReplacedByAConsolidatedOne()
    {
        var currentOnly = ToolCountTests.DeclaredTools()
            .Where(n => Toolsets.Includes(McpToolset.Current, n) && !Toolsets.Includes(McpToolset.Consolidated, n))
            .Order(StringComparer.Ordinal);

        Assert.Equal(currentOnly, ConsolidatedNames.ReplacedBy.Keys.Order(StringComparer.Ordinal));
        Assert.All(ConsolidatedNames.ReplacedBy.Values, to => Assert.Contains(to, Toolsets.Consolidated));
    }

    [Theory]
    [InlineData("Find it by name with mlqt_search_classes.", "Find it by name with mlqt_find.")]
    [InlineData("Run mlqt_analyze_dependencies first.", "Run mlqt_dependencies first.")]
    [InlineData("Use mlqt_set_component_modifier or mlqt_remove_component first.", "Use mlqt_edit_class first.")]
    [InlineData("mlqt_check_class + mlqt_spell_check", "mlqt_check + mlqt_check")]
    [InlineData("mlqt_find and mlqt_load stay as they are.", "mlqt_find and mlqt_load stay as they are.")]
    [InlineData("no tool named here", "no tool named here")]
    public void ARewrite_NamesTheConsolidatedTool_AndSaysItOnce(string text, string expected)
    {
        Assert.Equal(expected, ConsolidatedNames.Rewrite(text));
    }

    [Fact]
    public async Task AConsolidatedServersAnswer_NamesOnlyItsTools()
    {
        // A replace that would rename the class is refused, pointing at the tool that renames.
        var answer = await RenamingEditAnswerAsync(McpToolset.Consolidated);

        Assert.Contains("mlqt_refactor_class", answer);
        Assert.DoesNotContain("mlqt_rename_class", answer);
    }

    [Fact]
    public async Task ACurrentServersAnswer_IsAsItWas()
    {
        var answer = await RenamingEditAnswerAsync(McpToolset.Current);

        Assert.Contains("mlqt_rename_class", answer);
    }

    private static async Task<string> RenamingEditAnswerAsync(McpToolset toolset)
    {
        await using var server = await InProcessServer.StartAsync(new MlqtMcpServerOptions { Toolset = toolset });
        var created = await server.CallAsync("mlqt_create_library",
            new() { ["name"] = "Lib", ["directory"] = server.TempDirectory() });
        Assert.NotEqual(true, created.IsError);

        // mlqt_edit_class is in both sets, so the same call answers in each.
        var edit = await server.CallAsync("mlqt_edit_class", new()
        {
            ["classId"] = "Lib",
            ["operations"] = new[] { new Dictionary<string, object> { ["op"] = "replace", ["old"] = "Lib", ["new"] = "Renamed", ["all"] = true } },
        });
        var text = MlqtMcpServerExtensionsTests.Text(edit);
        Assert.Contains("would rename the class", text);
        return text;
    }
}
