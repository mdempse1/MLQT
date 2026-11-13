using System.Text.Json;
using MLQT.McpServer.Services;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <see cref="ToolCallGate"/>: reads together, every other call alone, in the order they arrived.
/// </summary>
public sealed class ToolCallGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsRunTogether()
    {
        var gate = new ToolCallGate();

        using var first = await gate.EnterAsync(shared: true, Ct);
        var second = gate.EnterAsync(shared: true, Ct);

        Assert.True(second.IsCompletedSuccessfully);
        (await second).Dispose();
    }

    [Fact]
    public async Task AChangeWaitsForTheReadsBeforeIt()
    {
        var gate = new ToolCallGate();

        var read = await gate.EnterAsync(shared: true, Ct);
        var change = gate.EnterAsync(shared: false, Ct);
        Assert.False(change.IsCompleted);

        read.Dispose();
        (await change.WaitAsync(TimeSpan.FromSeconds(5), Ct)).Dispose();
    }

    [Fact]
    public async Task AReadWaitsForTheChangeBeforeIt()
    {
        var gate = new ToolCallGate();

        var change = await gate.EnterAsync(shared: false, Ct);
        var read = gate.EnterAsync(shared: true, Ct);
        Assert.False(read.IsCompleted);

        change.Dispose();
        (await read.WaitAsync(TimeSpan.FromSeconds(5), Ct)).Dispose();
    }

    [Fact]
    public async Task TwoChangesRunOneAfterTheOther()
    {
        var gate = new ToolCallGate();

        var first = await gate.EnterAsync(shared: false, Ct);
        var second = gate.EnterAsync(shared: false, Ct);
        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(5), Ct)).Dispose();
    }

    /// <summary>
    /// The case that drew dashed boxes: a reload sent while a read was running, and a diagram sent after
    /// the reload. The diagram must not slip in beside the first read ahead of the reload.
    /// </summary>
    [Fact]
    public async Task AReadArrivingAfterAWaitingChangeWaitsBehindIt()
    {
        var gate = new ToolCallGate();

        var earlierRead = await gate.EnterAsync(shared: true, Ct);
        var reload = gate.EnterAsync(shared: false, Ct);
        var diagram = gate.EnterAsync(shared: true, Ct);
        Assert.False(reload.IsCompleted);
        Assert.False(diagram.IsCompleted);

        earlierRead.Dispose();
        var reloading = await reload.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.False(diagram.IsCompleted);

        reloading.Dispose();
        (await diagram.WaitAsync(TimeSpan.FromSeconds(5), Ct)).Dispose();
        Assert.Equal((0, false, 0), gate.State);
    }

    [Fact]
    public async Task ReadsQueuedTogetherAreAdmittedTogether()
    {
        var gate = new ToolCallGate();

        var change = await gate.EnterAsync(shared: false, Ct);
        var a = gate.EnterAsync(shared: true, Ct);
        var b = gate.EnterAsync(shared: true, Ct);

        change.Dispose();
        var readA = await a.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var readB = await b.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal((2, false, 0), gate.State);

        readA.Dispose();
        readB.Dispose();
    }

    [Fact]
    public async Task ACallCancelledWhileWaitingLeavesTheQueue()
    {
        var gate = new ToolCallGate();
        using var cancel = new CancellationTokenSource();

        var read = await gate.EnterAsync(shared: true, Ct);
        var change = gate.EnterAsync(shared: false, cancel.Token);
        var laterRead = gate.EnterAsync(shared: true, Ct);
        Assert.False(laterRead.IsCompleted);

        // The change gives up: the read behind it was waiting only for it, and goes in beside the first.
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => change);
        (await laterRead.WaitAsync(TimeSpan.FromSeconds(5), Ct)).Dispose();

        read.Dispose();
        Assert.Equal((0, false, 0), gate.State);
    }

    [Fact]
    public async Task EndingATurnTwiceGivesItBackOnce()
    {
        var gate = new ToolCallGate();

        var first = await gate.EnterAsync(shared: true, Ct);
        var second = await gate.EnterAsync(shared: true, Ct);
        first.Dispose();
        first.Dispose();

        Assert.Equal((1, false, 0), gate.State);
        second.Dispose();
    }

    [Theory]
    [InlineData("mlqt_get_class")]
    [InlineData("mlqt_get_diagram_image")]
    [InlineData("mlqt_get_guidance")]
    [InlineData("mlqt_find")]
    [InlineData("mlqt_browse")]
    [InlineData("mlqt_list_findings")]
    public void TheReadingToolsShare(string tool) =>
        Assert.True(ToolCallGate.IsShared(tool, null));

    /// <summary>
    /// Everything that changes the session, the ones marked <c>ReadOnly</c> because they leave the user's
    /// files alone included, and a tool the gate knows nothing about.
    /// </summary>
    [Theory]
    [InlineData("mlqt_load")]
    [InlineData("mlqt_check")]
    [InlineData("mlqt_dependencies")]
    [InlineData("mlqt_edit_class")]
    [InlineData("mlqt_write_class")]
    [InlineData("mlqt_refactor_class")]
    [InlineData("mlqt_format")]
    [InlineData("mlqt_create_library")]
    [InlineData("mlqt_style_settings")]
    [InlineData("host_tool")]
    [InlineData(null)]
    public void EveryOtherToolRunsAlone(string? tool) =>
        Assert.False(ToolCallGate.IsShared(tool, null));

    [Theory]
    [InlineData(null, true)]
    [InlineData("\"status\"", true)]
    [InlineData("\" Status \"", true)]
    [InlineData("\"\"", true)]
    [InlineData("null", true)]
    [InlineData("\"reload\"", false)]
    [InlineData("\"unload\"", false)]
    [InlineData("\"reset\"", false)]
    [InlineData("5", false)]
    public void TheSessionToolSharesOnlyForItsStatus(string? action, bool shared)
    {
        var arguments = new Dictionary<string, JsonElement>();
        if (action is not null)
            arguments["action"] = JsonDocument.Parse(action).RootElement.Clone();

        Assert.Equal(shared, ToolCallGate.IsShared("mlqt_session", arguments));
    }

    /// <summary>
    /// mlqt_get_class shares unless it asks for its resources view, which runs the dependency and
    /// resource analysis when the session has not and writes into the graph while other reads run.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("null", true)]
    [InlineData("[\"info\", \"source\"]", true)]
    [InlineData("[\"layout\"]", true)]
    [InlineData("[\"resources\"]", false)]
    [InlineData("[\"source\", \"Resources\"]", false)]
    [InlineData("\"[\\\"resources\\\"]\"", false)]   // views sent as one string
    public void GetClassSharesUnlessItAsksForResources(string? views, bool shared)
    {
        var arguments = new Dictionary<string, JsonElement>();
        if (views is not null)
            arguments["views"] = JsonDocument.Parse(views).RootElement.Clone();

        Assert.Equal(shared, ToolCallGate.IsShared("mlqt_get_class", arguments));
    }

    /// <summary>
    /// Every tool the server serves is decided on purpose: a new reading tool left off the list runs
    /// alone, which is safe and slower, so this names the ones that are expected to share.
    /// </summary>
    [Fact]
    public void TheSharedListNamesOnlyServedTools()
    {
        var served = ToolCountTests.DeclaredTools().ToHashSet(StringComparer.Ordinal);
        var shared = served.Where(t => ToolCallGate.IsShared(t, null)).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            ["mlqt_browse", "mlqt_find", "mlqt_get_class", "mlqt_get_diagram_image", "mlqt_get_guidance",
             "mlqt_list_findings", "mlqt_session"],
            shared);
    }
}
