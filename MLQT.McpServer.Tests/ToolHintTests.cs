namespace MLQT.McpServer.Tests;

/// <summary>
/// The 16 tools (B503), and what each says it does to its world: a client may run a read-only one
/// without asking, and asks before a destructive one.
/// </summary>
public sealed class ToolHintTests
{
    // From the Writes column of the consolidation plan. OpenWorld is false for all: each works on the local
    // files and session only.
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
    public async Task EachTool_SaysWhatItChanges(string name, bool readOnly, bool destructive, bool idempotent)
    {
        await using var server = await InProcessServer.StartAsync();
        var tool = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Single(t => t.Name == name);

        var hints = tool.ProtocolTool.Annotations!;
        Assert.Equal(readOnly, hints.ReadOnlyHint);
        Assert.False(hints.OpenWorldHint);
        if (!readOnly)   // the other two mean something only for a tool that changes things
            Assert.Equal((destructive, idempotent), (hints.DestructiveHint, hints.IdempotentHint));
    }

    // The names are the promise a host builds on, so the set is written out, not read back from the server.
    [Fact]
    public void TheServerDeclaresExactlyTheSixteenTools_EachWithAHintCase()
    {
        var cases = typeof(ToolHintTests).GetMethod(nameof(EachTool_SaysWhatItChanges))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false).Cast<InlineDataAttribute>()
            .Select(d => (string)d.Data[0]!)
            .ToList();

        Assert.Equal(16, cases.Count);
        Assert.Equal(cases.Order(StringComparer.Ordinal), ToolCountTests.DeclaredTools().Order(StringComparer.Ordinal));
    }
}
