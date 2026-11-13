using System.Text.Json;

namespace MLQT.McpServer.Services;

/// <summary>
/// Keeps a tool call that changes the session from running beside any other: the reads run together,
/// everything else runs alone, and calls are admitted in the order they arrive.
/// </summary>
/// <remarks>
/// <para><b>Why the server needs it.</b> The SDK runs every request as it arrives, and a client sends the
/// tool calls of one message together - Claude Code does. <see cref="MLQT.Services.Interfaces.ILibraryDataService"/>
/// promises its reads are safe only while nothing loads, reloads or edits, and nothing kept that promise
/// here. An agent that sent a repository reload and a diagram in one message got the picture drawn
/// while the reload had the library's classes out of the graph: components whose types were "not
/// loaded", drawn as dashed boxes, the front pair of a driveline drawn and the rear pair not, and a
/// "No class X. Did you mean X?" for a class that was missing for the lookup and back for the
/// suggestion.</para>
/// <para><b>In the order they arrive</b>, so a call sent after a reload sees what the reload did: a read
/// waits behind a change that came before it, and a change waits for the reads before it to finish.
/// Reads that arrive together still run together.</para>
/// <para><b>Which calls may share</b> is <see cref="IsShared"/>'s list, and anything not on it runs alone -
/// a host's own tools included, since this cannot tell what they touch. The tools' <c>ReadOnly</c>
/// annotations do not answer it: they say whether the user's files change, and <c>mlqt_load</c> and
/// <c>mlqt_session</c> are marked read-only while each replaces the graph.</para>
/// </remarks>
internal sealed class ToolCallGate
{
    // The calls that only read the loaded libraries and the findings already recorded. mlqt_check is not
    // one: a check records findings and can run the dependency analysis, as mlqt_dependencies does - and
    // so can mlqt_get_class's resources view, which IsShared asks about separately.
    private static readonly HashSet<string> ReadingTools = new(StringComparer.Ordinal)
    {
        "mlqt_get_class",
        "mlqt_get_diagram_image",
        "mlqt_get_guidance",
        "mlqt_find",
        "mlqt_browse",
        "mlqt_list_findings",
    };

    private readonly object _lock = new();
    private readonly LinkedList<Waiter> _queue = new();
    private int _readers;
    private bool _writer;

    /// <summary>
    /// Whether a call of <paramref name="tool"/> with <paramref name="arguments"/> only reads, and may run
    /// beside other reads. <c>mlqt_session</c> reads only for its <c>status</c> action, which is its default.
    /// </summary>
    public static bool IsShared(string? tool, IDictionary<string, JsonElement>? arguments)
    {
        if (tool is null)
            return false;
        if (string.Equals(tool, "mlqt_get_class", StringComparison.Ordinal))
            return !AsksForResources(arguments);
        if (ReadingTools.Contains(tool))
            return true;
        if (!string.Equals(tool, "mlqt_session", StringComparison.Ordinal))
            return false;

        if (arguments is null || !arguments.TryGetValue("action", out var action)
            || action.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;
        return action.ValueKind == JsonValueKind.String
            && action.GetString()?.Trim() is { } text
            && (text.Length == 0 || string.Equals(text, "status", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether an <c>mlqt_get_class</c> call asks for its <c>resources</c> view, which runs the
    /// dependency and resource analysis when the session has not - writing edges and resource nodes
    /// into the graph, as <c>mlqt_check</c> and <c>mlqt_dependencies</c> do - so it runs alone.
    /// Read from the raw JSON, whether the views came as an array or as one string: taking a read
    /// for a change only costs it its turn, the reverse is the race this gate is for.
    /// </summary>
    private static bool AsksForResources(IDictionary<string, JsonElement>? arguments) =>
        arguments is not null
        && arguments.TryGetValue("views", out var views)
        && views.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
        && views.GetRawText().Contains("resources", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Waits for this call's turn and returns what ends it. A call cancelled while it waits leaves the
    /// queue, and the calls behind it go on.
    /// </summary>
    public async Task<IDisposable> EnterAsync(bool shared, CancellationToken cancellationToken)
    {
        Waiter waiter;
        lock (_lock)
        {
            // Straight in when nothing is queued ahead of it, so an idle server costs nothing.
            if (_queue.Count == 0 && CanEnter(shared))
            {
                Take(shared);
                return new Release(this, shared);
            }

            waiter = new Waiter(shared);
            waiter.Node = _queue.AddLast(waiter);
        }

        using (cancellationToken.Register(() => Abandon(waiter)))
            await waiter.Turn.Task.ConfigureAwait(false);
        return new Release(this, shared);
    }

    /// <summary>For tests: how many calls are running and waiting.</summary>
    internal (int Readers, bool Writer, int Waiting) State
    {
        get
        {
            lock (_lock)
                return (_readers, _writer, _queue.Count);
        }
    }

    private bool CanEnter(bool shared) => shared ? !_writer : !_writer && _readers == 0;

    private void Take(bool shared)
    {
        if (shared)
            _readers++;
        else
            _writer = true;
    }

    private void Leave(bool shared)
    {
        lock (_lock)
        {
            if (shared)
                _readers--;
            else
                _writer = false;
            AdmitFromTheFront();
        }
    }

    // Admits from the head of the queue for as long as the head may enter, and no further: a read behind
    // a waiting change waits with it, so the order calls arrived in is the order they see the session in.
    // Callers hold _lock.
    private void AdmitFromTheFront()
    {
        while (_queue.First is { } first && CanEnter(first.Value.Shared))
        {
            _queue.RemoveFirst();
            first.Value.Node = null;
            Take(first.Value.Shared);
            first.Value.Turn.TrySetResult();
        }
    }

    private void Abandon(Waiter waiter)
    {
        lock (_lock)
        {
            // Already admitted: the call owns its turn, and gives it back through Release.
            if (waiter.Node is null)
                return;
            _queue.Remove(waiter.Node);
            waiter.Node = null;
            waiter.Turn.TrySetCanceled();

            // A change at the head may have been holding back the reads behind it.
            AdmitFromTheFront();
        }
    }

    private sealed class Waiter(bool shared)
    {
        public bool Shared { get; } = shared;
        public TaskCompletionSource Turn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter>? Node { get; set; }
    }

    private sealed class Release(ToolCallGate gate, bool shared) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                gate.Leave(shared);
        }
    }
}
