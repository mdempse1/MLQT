using DymolaInterface;
using DymolaInterface.Interfaces;
using MLQT.Services.Interfaces;
using OpenModelicaInterface;
using OpenModelicaInterface.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// A Dymola or OpenModelica session that is not installed: the small part of each tool's API that
/// the checking services use, with the behaviour a test wants to vary exposed as fields.
/// </summary>
/// <remarks>
/// <para><b>It models the log buffer, not just the answers.</b> Most of what
/// <see cref="ModelCheckingServiceContract"/> asserts is about which check's output a result ends
/// up carrying, and a stub returning a fixed string cannot tell a drained buffer from an undrained
/// one. So <see cref="Buffer"/> accumulates what <see cref="Says"/> produces, and each tool's fake
/// empties it where the real tool would — Dymola on <c>clearLog</c>, omc on every read of the error
/// string.</para>
/// </remarks>
public abstract class FakeTool
{
    /// <summary>What the tool would report next. Seed it to stand for an earlier command's output.</summary>
    public string Buffer = "";

    /// <summary>Whether a given class checks. Everything checks by default.</summary>
    public Func<string, bool>? Checks;

    /// <summary>What checking a given class adds to the log. Nothing by default.</summary>
    public Func<string, string>? Says;

    /// <summary>Whether a given file opens. Everything opens by default.</summary>
    public Func<string, bool>? Opens;

    /// <summary>What opening a given file adds to the log. Nothing by default.</summary>
    public Func<string, string>? OpenSays;

    /// <summary>Thrown from the check, after whatever <see cref="Says"/> produced has been logged.</summary>
    public Exception? ThrowOnCheck;

    /// <summary>Thrown from every read of the log.</summary>
    public Exception? ThrowOnRead;

    /// <summary>Whether a given class runs out of time. Each tool reports that its own way: Dymola
    /// answers nothing and says so in its outcome, omc throws <see cref="TimeoutException"/>.</summary>
    public Func<string, bool>? TimesOut;

    /// <summary>Whether opening a given file runs out of time, reported the same ways.</summary>
    public Func<string, bool>? OpenTimesOut;

    /// <summary>A check that is still running when the caller cancels it — the only way it ends.</summary>
    public bool WaitsForCancel;

    /// <summary>Whether the tool goes away - window closed, process exited - while checking a given
    /// class. Once it has, nothing it is asked afterwards gets an answer.</summary>
    public Func<string, bool>? DiesOn;

    /// <summary>The tool has gone.</summary>
    public bool Dead;

    /// <summary>Each call, as a verb: <c>open</c>, <c>clear</c>, <c>clearLog</c>, <c>check</c>, <c>read</c>.</summary>
    public readonly List<string> Calls = [];

    public int OpenAttempts => Calls.Count(c => c == "open");
    public int Clears => Calls.Count(c => c == "clear");
    public int ChecksRun => Calls.Count(c => c == "check");

    protected bool Open(string path)
    {
        Calls.Add("open");
        Buffer += OpenSays?.Invoke(path) ?? "";
        return Opens?.Invoke(path) ?? true;
    }

    protected bool Check(string modelId)
    {
        Calls.Add("check");
        Buffer += Says?.Invoke(modelId) ?? "";
        if (ThrowOnCheck is not null)
            throw ThrowOnCheck;
        return Checks?.Invoke(modelId) ?? true;
    }

    protected string Read(bool draining)
    {
        Calls.Add("read");
        if (ThrowOnRead is not null)
            throw ThrowOnRead;

        var said = Buffer;
        if (draining)
            Buffer = "";
        return said;
    }
}

/// <summary>
/// Dymola's log accumulates until <c>clearLog</c> empties it, and <c>clear</c> unloads the classes
/// without touching the log.
/// </summary>
public sealed class FakeDymola : FakeTool, IDymolaInterface
{
    /// <summary>The flags of every <c>openModel</c>, which decide what else Dymola does.</summary>
    public readonly List<(bool MustRead, bool ChangeDirectory)> OpenFlags = [];

    /// <summary>The flags of the last <c>checkModel</c> — <c>simulate</c> is not a check.</summary>
    public (bool Simulate, bool Constraint) LastCheckFlags;

    /// <summary>What the real interface reports about the last command: answered, or not.</summary>
    public CommandOutcome LastOutcome { get; private set; } = CommandOutcome.Answered;

    /// <summary>What an ordinary check reports as its outcome: Answered, unless a test stands for
    /// Dymola reporting an error about the command, which is Failed with Dymola still there.</summary>
    public CommandOutcome CheckOutcome = CommandOutcome.Answered;

    /// <summary>What a probe of the session would find. Answering unless a test says otherwise.</summary>
    public DymolaSessionState State = DymolaSessionState.Answering;

    public Task<DymolaSessionState> GetSessionStateAsync()
    {
        Calls.Add("probe");
        return Task.FromResult(State);
    }

    public Task<bool> OpenModelAsync(string path, bool mustRead = true, bool changeDirectory = true,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        OpenFlags.Add((mustRead, changeDirectory));
        if (Dead)
        {
            Calls.Add("open");
            LastOutcome = CommandOutcome.Failed;
            return Task.FromResult(false);
        }

        if (OpenTimesOut?.Invoke(path) == true)
        {
            Calls.Add("open");
            LastOutcome = CommandOutcome.TimedOut;
            return Task.FromResult(false);
        }

        LastOutcome = CommandOutcome.Answered;
        return Task.FromResult(Open(path));
    }

    public async Task<bool> CheckModelAsync(string problem, bool simulate = false, bool constraint = false,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        LastCheckFlags = (simulate, constraint);
        if (WaitsForCancel)
        {
            Calls.Add("check");
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                LastOutcome = CommandOutcome.Cancelled;
                return false;
            }
        }

        if (TimesOut?.Invoke(problem) == true)
        {
            Calls.Add("check");
            LastOutcome = CommandOutcome.TimedOut;
            return false;
        }

        // A closed window: the connection is refused, which the real interface reports as Failed -
        // nothing marks a session offline partway through.
        if (Dead || DiesOn?.Invoke(problem) == true)
        {
            Calls.Add("check");
            Dead = true;
            State = DymolaSessionState.Gone;
            LastOutcome = CommandOutcome.Failed;
            return false;
        }

        LastOutcome = CheckOutcome;
        return Check(problem);
    }

    public Task<bool> ClearAsync(bool fast = false)
    {
        Calls.Add("clear");
        LastOutcome = CommandOutcome.Answered;
        return Task.FromResult(true);
    }

    public Task<bool> ClearLogAsync()
    {
        Calls.Add("clearLog");
        if (Dead)
        {
            LastOutcome = CommandOutcome.Failed;
            return Task.FromResult(false);
        }

        Buffer = "";
        LastOutcome = CommandOutcome.Answered;
        return Task.FromResult(true);
    }

    public Task<string> GetLastErrorAsync()
    {
        if (Dead)
        {
            Calls.Add("read");
            LastOutcome = CommandOutcome.Failed;
            return Task.FromResult("");
        }

        var said = Read(draining: false);
        LastOutcome = CommandOutcome.Answered;
        return Task.FromResult(said);
    }
}

/// <summary>
/// omc has no separate clear-the-log command: <c>getErrorString</c> returns the accumulated
/// messages <b>and empties the buffer</b>, so reading it is how the service drains it.
/// </summary>
public sealed class FakeOpenModelica : FakeTool, IOpenModelicaInterface
{
    public Task<bool> LoadFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (Dead)
        {
            Calls.Add("open");
            throw new OpenModelicaExitedException("the fake omc has exited");
        }

        if (OpenTimesOut?.Invoke(filePath) == true)
        {
            Calls.Add("open");
            throw new TimeoutException("the fake omc ran out of time opening the file");
        }

        return Task.FromResult(Open(filePath));
    }

    public async Task<bool> CheckModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        if (WaitsForCancel)
        {
            Calls.Add("check");
            await Task.Delay(Timeout.Infinite, cancellationToken);   // throws, as the real one does
        }

        if (TimesOut?.Invoke(modelName) == true)
        {
            Calls.Add("check");
            throw new TimeoutException("the fake omc ran out of time checking");
        }

        if (Dead || DiesOn?.Invoke(modelName) == true)
        {
            Calls.Add("check");
            Dead = true;
            throw new OpenModelicaExitedException("the fake omc exited before it answered 'checkModel'");
        }

        return Check(modelName);
    }

    public Task<string> GetErrorStringAsync() => Task.FromResult(Read(draining: true));

    public Task<bool> ClearAsync()
    {
        Calls.Add("clear");
        return Task.FromResult(true);
    }
}

/// <summary>
/// One checking service, its fake tool, and the factory between them — so a contract test can be
/// written once and run against both tools.
/// </summary>
public abstract class ToolHarness
{
    public abstract IModelCheckingService Service { get; }
    public abstract FakeTool Tool { get; }
    public abstract string ToolName { get; }

    /// <summary>How many times the service has asked the factory for a session.</summary>
    public abstract int Connections { get; }

    /// <summary>How many times the service has told the factory to drop its session.</summary>
    public abstract int Resets { get; }

    /// <summary>Makes the tool unavailable, as an uninstalled one is.</summary>
    public abstract void FailToConnect(Exception failure);

    /// <summary>Makes the tool take for ever to start: only the caller's token ends the wait.</summary>
    public abstract void StartSlowly();
}

public sealed class DymolaHarness : ToolHarness
{
    private readonly FakeDymola _tool = new();
    private readonly Factory _factory;
    private readonly DymolaCheckingService _service;

    public DymolaHarness()
    {
        _factory = new Factory(_tool);
        _service = new DymolaCheckingService(_factory);
    }

    public override IModelCheckingService Service => _service;
    public override FakeTool Tool => _tool;
    public override string ToolName => "Dymola";
    public override int Connections => _factory.Connections;
    public override int Resets => _factory.Resets;
    public override void FailToConnect(Exception failure) => _factory.Failure = failure;
    public override void StartSlowly() => _factory.Slow = true;

    private sealed class Factory(IDymolaInterface session) : IDymolaInterfaceFactory
    {
        public Exception? Failure;
        public bool Slow;
        public int Connections;
        public int Resets;

        public bool IsConnected => Failure is null;

        public async Task<IDymolaInterface> GetOrCreateAsync(CancellationToken cancellationToken = default)
        {
            Connections++;
            if (Slow)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return Failure is null ? session : throw Failure;
        }

        public Task ResetAsync()
        {
            Resets++;
            return Task.CompletedTask;
        }

        public void UpdateSettings(DymolaSettings settings) { }

        public void Shutdown() { }
    }
}

public sealed class OpenModelicaHarness : ToolHarness
{
    private readonly FakeOpenModelica _tool = new();
    private readonly Factory _factory;
    private readonly OpenModelicaCheckingService _service;

    public OpenModelicaHarness()
    {
        _factory = new Factory(_tool);
        _service = new OpenModelicaCheckingService(_factory);
    }

    public override IModelCheckingService Service => _service;
    public override FakeTool Tool => _tool;
    public override string ToolName => "OpenModelica";
    public override int Connections => _factory.Connections;
    public override int Resets => _factory.Resets;
    public override void FailToConnect(Exception failure) => _factory.Failure = failure;
    public override void StartSlowly() => _factory.Slow = true;

    private sealed class Factory(IOpenModelicaInterface session) : IOpenModelicaInterfaceFactory
    {
        public Exception? Failure;
        public bool Slow;
        public int Connections;
        public int Resets;

        public bool IsConnected => Failure is null;

        public async Task<IOpenModelicaInterface> GetOrCreateAsync(CancellationToken cancellationToken = default)
        {
            Connections++;
            if (Slow)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return Failure is null ? session : throw Failure;
        }

        public Task ResetAsync()
        {
            Resets++;
            return Task.CompletedTask;
        }

        public void UpdateSettings(OpenModelicaSettings settings) { }

        public void Shutdown() { }
    }
}
