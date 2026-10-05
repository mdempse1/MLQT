using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NetMQ;
using OpenModelicaInterface.Interfaces;
using NetMQ.Sockets;

namespace OpenModelicaInterface;

/// <summary>
/// Interface to OpenModelica Compiler (OMC) using ZeroMQ (ZMQ) communication.
/// This class provides a C# wrapper around OMC's scripting API.
/// Based on OMPython's approach using ZMQ REQ-REP pattern.
/// </summary>
public class OpenModelicaInterface : IOpenModelicaInterface, IDisposable
{
    private readonly string _omcPath;
    private Process? _omcProcess;
    private RequestSocket? _socket;
    private bool _isDisposed;
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    /// <summary>
    /// Cancelled by <see cref="Dispose"/>, so a command still waiting on omc lets go of the socket
    /// before it is disposed rather than being torn down underneath (B369).
    /// </summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>The port to ask for that lets omc bind any free one, and say which.</summary>
    public const int AnyPort = 0;

    private readonly int _port;

    /// <summary>
    /// Creates a new OpenModelica interface instance.
    /// </summary>
    /// <param name="omcPath">Path to omc.exe (e.g., "C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe")</param>
    /// <param name="port">ZMQ port omc listens on, or <see cref="AnyPort"/> (the default) for any
    /// free one - see <see cref="Port"/> for the one it took.</param>
    public OpenModelicaInterface(string omcPath, int port = AnyPort)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        _omcPath = omcPath;
        _port = port;
    }

    /// <summary>
    /// The port omc is listening on, once started - the one it chose when started with
    /// <see cref="AnyPort"/>. Null before a start, and after the session has closed.
    /// </summary>
    public int? Port => IsConnected ? _boundPort : null;

    private int? _boundPort;

    /// <summary>
    /// A session that owns <paramref name="process"/> as though it had started it, and is not
    /// connected - so what disposing a session does to the process tree behind it can be tested
    /// without an omc (B493).
    /// </summary>
    internal OpenModelicaInterface(Process process)
        : this(string.Empty)
    {
        _omcProcess = process;
    }

    /// <summary>
    /// Checks if OMC is connected via ZMQ.
    /// </summary>
    public bool IsConnected => _socket != null && !_isDisposed;

    /// <summary>The omc process this session started, while it has one - for tests that end it.</summary>
    internal int? ProcessId => _omcProcess is { HasExited: false } process ? process.Id : null;

    /// <summary>
    /// How long one command may take before the session is given up on; <see cref="Timeout.InfiniteTimeSpan"/>
    /// for no limit (B263).
    /// </summary>
    /// <remarks>
    /// <para><b>A command that runs out of time ends the session</b>, not just the wait. omc is
    /// spoken to over a ZeroMQ REQ socket, which must receive the reply to one request before it may
    /// send the next — so once a reply has been given up on the socket is unusable, and omc is still
    /// working on the abandoned command anyway. The socket is closed and omc killed, <see cref="IsConnected"/>
    /// turns false, and the factory starts a fresh session for the next caller.</para>
    ///
    /// <para>Before this there was no limit at all: the receive blocked until omc answered, so a
    /// long check hung its caller indefinitely, and <c>OpenModelicaSettings.CommandTimeoutMs</c> was a
    /// setting nothing read.</para>
    /// </remarks>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long omc may take to start and answer its first command. Replaces a fixed two-second
    /// sleep, which was both too long for a fast start and no bound at all on a slow one — the
    /// version query after it waited as long as omc took.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Starts the OMC process and establishes ZMQ connection.
    /// </summary>
    /// <param name="cancellationToken">Gives up on the start; omc is stopped.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        if (string.IsNullOrEmpty(_omcPath) || !File.Exists(_omcPath))
        {
            throw new FileNotFoundException($"OMC executable not found at: {_omcPath}");
        }

        // Start OMC with ZMQ server. Without --interactivePort omc binds any free port; either way it
        // says where it is listening in a port file named after the suffix (see OmcPortAnnouncement),
        // so two sessions - the GUI and the MCP server, say - never contend for one fixed port.
        var announcement = new OmcPortAnnouncement($"mlqt-{Guid.NewGuid():N}");
        var startInfo = new ProcessStartInfo
        {
            FileName = _omcPath,
            Arguments = StartArguments(announcement.Suffix, _port),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Held in a local from here on: Abandon or Dispose may clear the field at any moment (see
        // Abandon), and the readers below run later still.
        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("Failed to start OMC process");
        _omcProcess = process;

        // Disposed while starting - MLQT exiting as a check starts omc (B493). Dispose may already
        // have been through the process, so the one just started is ended here or by nobody - unless
        // Dispose took it after all, in which case it is Dispose's to end.
        if (_isDisposed)
        {
            if (TakeProcess() is { } orphan)
            {
                try { EndProcessTree(orphan); } catch { /* gone already */ }
                orphan.Dispose();
            }
            throw new ObjectDisposedException(nameof(OpenModelicaInterface));
        }

        // Background readers consume stdout/stderr (prevent blocking), and hand what omc says to the
        // announcement until it has said where it is listening.
        _ = Task.Run(() => ConsumeStreamAsync(process.StandardOutput, announcement.Output, announcement.Ended));
        _ = Task.Run(() => ConsumeStreamAsync(process.StandardError, announcement.Error, null));

        // One clock for the whole start: finding the port and the first answer share StartupTimeout.
        var clock = Stopwatch.StartNew();
        var endpoint = await WaitForEndpointAsync(announcement, cancellationToken);

        try
        {
            _socket = new RequestSocket();
            _socket.Connect(endpoint);
            _boundPort = OmcPortAnnouncement.PortOf(endpoint);
        }
        catch (Exception ex)
        {
            Abandon();
            throw new InvalidOperationException($"Failed to connect to OMC at {endpoint}", ex);
        }

        // Verify connection by getting version, within what is left of StartupTimeout.
        var left = StartupTimeout == Timeout.InfiniteTimeSpan
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond, (StartupTimeout - clock.Elapsed).Ticks));
        string version;
        try
        {
            version = UnquoteString(await SendCommandAsync("getVersion()", left, "start", cancellationToken));
        }
        catch (TimeoutException)
        {
            throw;   // already says what happened, and the session has been closed
        }
        catch (OperationCanceledException)
        {
            Abandon();   // not left half-started: the next start would find the port taken
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("OMC started but failed to respond to commands", ex);
        }

        if (string.IsNullOrEmpty(version))
        {
            Abandon();
            throw new InvalidOperationException("Failed to establish communication with OMC");
        }
    }

    /// <summary>omc's command line: ZeroMQ, the port file's suffix, and the port when one is fixed.</summary>
    internal static string StartArguments(string suffix, int port) => port == AnyPort
        ? $"--interactive=zmq -z={suffix}"
        : $"--interactive=zmq -z={suffix} --interactivePort={port}";

    /// <summary>
    /// The address omc announces, within <see cref="StartupTimeout"/>. The session is closed if it
    /// does not come.
    /// </summary>
    private async Task<string> WaitForEndpointAsync(OmcPortAnnouncement announcement, CancellationToken cancellationToken)
    {
        // Disposing the session ends the wait as well as the caller's token: MLQT exiting as omc starts.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var portFile = await announcement.PortFile.WaitAsync(StartupTimeout, stopping.Token);
            return ReadPortFile(portFile);
        }
        catch (TimeoutException)
        {
            Abandon();
            throw new TimeoutException(StartTimedOut());
        }
        catch (OperationCanceledException)
        {
            Abandon();   // not left half-started
            if (_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new ObjectDisposedException(nameof(OpenModelicaInterface));
            throw;
        }
        catch
        {
            Abandon();
            throw;
        }
    }

    /// <summary>
    /// The address in the port file omc announced. The file is deleted once read: omc leaves it behind
    /// when it exits, and with a fresh suffix every start nothing would ever read it again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file is missing or does not hold an address.</exception>
    internal static string ReadPortFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"OpenModelica's port file {path} could not be read.", ex);
        }

        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp */ }

        return OmcPortAnnouncement.Endpoint(text)
               ?? throw new InvalidOperationException(
                   $"OpenModelica's port file {path} does not hold an address: '{text.Trim()}'.");
    }

    private string StartTimedOut() =>
        $"OpenModelica did not start and answer within {StartupTimeout.TotalSeconds:0.#}s; it has been stopped.";

    /// <summary>
    /// Consumes a stream asynchronously to prevent process blocking, handing what it reads to
    /// <paramref name="read"/> and saying when it ends.
    /// </summary>
    /// <remarks>
    /// Read in chunks, not lines: omc ends its port announcement without a newline (see
    /// <see cref="OmcPortAnnouncement"/>), so a line reader held it back until omc next printed.
    /// </remarks>
    private async Task ConsumeStreamAsync(StreamReader reader, Action<string> read, Action? ended)
    {
        var buffer = new char[4096];
        try
        {
            while (!_isDisposed)
            {
                var count = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    // End of stream: omc has exited. Looping on here spun a thread at full speed for
                    // as long as this object lived, which a session closed after a timeout does.
                    break;
                }

                var text = new string(buffer, 0, count);
                read(text);
                Debug.Write($"OMC: {text}");
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected when disposing
        }
        catch (IOException)
        {
            // The pipe broke as omc was ended
        }
        finally
        {
            ended?.Invoke();
        }
    }

    /// <summary>
    /// Sends a raw command to OMC and returns the response.
    /// </summary>
    /// <param name="command">The OMC command to execute</param>
    /// <returns>The response from OMC</returns>
    /// <param name="cancellationToken">Gives up on the command, whether still queued behind another
    /// or already sent. Once sent, giving up closes the session — see <see cref="CommandTimeout"/>.</param>
    /// <exception cref="TimeoutException">omc did not answer within <see cref="CommandTimeout"/>; the
    /// session has been closed.</exception>
    /// <exception cref="OperationCanceledException">The token fired.</exception>
    /// <exception cref="OpenModelicaExitedException">omc exited before it answered; the session has
    /// been closed.</exception>
    public Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
        => SendCommandAsync(command, CommandTimeout, "command", cancellationToken);

    private async Task<string> SendCommandAsync(
        string command, TimeSpan timeout, string what, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to OMC. Call StartAsync() first.");
        }

        // One token for the caller and for the session's own end: disposing it must be able to stop
        // a command that would otherwise wait on omc for as long as its limit allows.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        // Cancelled while queued: nothing has been sent, so the session is untouched.
        try
        {
            await _commandLock.WaitAsync(stopping.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(OpenModelicaInterface));
        }

        try
        {
            // Asked again under the lock: the session may have been closed while this waited for it.
            if (!IsConnected)
                throw new ObjectDisposedException(nameof(OpenModelicaInterface));

            // Read once each: a start being abandoned outside the lock can clear either at any time.
            var socket = _socket ?? throw new ObjectDisposedException(nameof(OpenModelicaInterface));
            var process = _omcProcess;

            // Sent and received against one clock, both in slices, so the wait can end on the time
            // limit or the token rather than only when omc answers. The send needs it as much as the
            // receive: a REQ socket with no peer blocks in SendFrame until one connects, so an omc
            // that never binds its port - or one still starting - held a bare send for as long as it
            // took, and a start-up limit on the receive alone bounded nothing.
            var response = await Task.Run(() => Exchange(
                socket, command, timeout, stopping.Token, () => process is { HasExited: true }));
            if (response is null)
            {
                Abandon();
                if (_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    throw new ObjectDisposedException(nameof(OpenModelicaInterface));
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(what == "start"
                    ? StartTimedOut()
                    :$"OpenModelica did not answer {Describe(command)} within {timeout.TotalSeconds:0.#}s; the session has been closed.");
            }

            return response;
        }
        catch (OpenModelicaExitedException)
        {
            Abandon();
            throw;
        }
        catch (Exception ex) when (ex is not TimeoutException and not OperationCanceledException
                                       and not ObjectDisposedException)
        {
            throw new InvalidOperationException($"Failed to send command to OMC: {command}", ex);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Sends <paramref name="command"/> and returns the reply, or null when the time limit passed or
    /// the token fired first - at either end.
    /// </summary>
    /// <exception cref="OpenModelicaExitedException"><paramref name="hasExited"/> said omc had gone
    /// before it answered. Asked between slices, because a REQ socket gives no sign of it: the request
    /// is queued for a peer that will never read it (B334).</exception>
    internal static string? Exchange(
        RequestSocket socket, string command, TimeSpan timeout, CancellationToken cancellationToken,
        Func<bool> hasExited)
    {
        var clock = Stopwatch.StartNew();

        var sent = false;
        while (!sent)
        {
            if (NextWait(timeout, clock, cancellationToken) is not { } wait)
                return null;
            ThrowIfExited(hasExited, command);
            sent = socket.TrySendFrame(wait, command);
        }

        while (true)
        {
            if (NextWait(timeout, clock, cancellationToken) is not { } wait)
                return null;
            if (socket.TryReceiveFrameString(wait, out var reply))
                return reply ?? "";
            ThrowIfExited(hasExited, command);
        }
    }

    private static void ThrowIfExited(Func<bool> hasExited, string command)
    {
        if (hasExited())
            throw new OpenModelicaExitedException(
                $"OpenModelica exited before it answered {Describe(command)}; the session has been closed.");
    }

    /// <summary>How long the next slice of a wait may be, or null when the wait is over.</summary>
    private static TimeSpan? NextWait(TimeSpan timeout, Stopwatch clock, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        var slice = TimeSpan.FromMilliseconds(100);
        if (timeout == Timeout.InfiniteTimeSpan)
            return slice;

        var left = timeout - clock.Elapsed;
        if (left <= TimeSpan.Zero)
            return null;
        if (left >= slice)
            return slice;

        // Never less than a millisecond. NetMQ takes the wait in whole milliseconds, and a remainder
        // under one truncates to zero - which it does not treat as "do not wait": measured, a 1 ms
        // start-up limit then waited out the whole start of omc.
        return left < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : left;
    }

    /// <summary>The command as a message can show it: its name, not a whole script.</summary>
    private static string Describe(string command)
    {
        var name = command.Split('(', 2)[0].Trim();
        return name.Length is > 0 and <= 60 ? $"'{name}'" : "a command";
    }

    /// <summary>
    /// Closes a session that can no longer be used: the REQ socket is stuck waiting for a reply that
    /// was given up on, and omc is still busy with the command behind it. Leaves the object undisposed
    /// but disconnected, so the factory replaces it.
    /// </summary>
    /// <remarks>
    /// Runs outside the command lock on the start-up paths, so it can meet <see cref="Dispose"/> -
    /// which cancels <see cref="_lifetime"/> and so is often the very thing that sent a start here.
    /// Each takes the socket and the process with <see cref="TakeSocket"/>/<see cref="TakeProcess"/>,
    /// so exactly one of them ends and disposes each, and neither reads a field the other has just
    /// cleared: reading <c>_omcProcess</c> again after a null check threw a
    /// <see cref="NullReferenceException"/> out of Dispose on a loaded CI runner. Internal so that race
    /// can be tested.
    /// </remarks>
    internal void Abandon()
    {
        try { TakeSocket()?.Dispose(); } catch { /* already unusable */ }

        if (TakeProcess() is { } process)
        {
            try
            {
                if (!process.HasExited)
                    EndProcessTree(process);
            }
            catch
            {
                // Gone already, or not ours to kill.
            }
            process.Dispose();
        }
    }

    /// <summary>The socket, now this caller's alone to close; null when another took it first.</summary>
    private RequestSocket? TakeSocket() => Interlocked.Exchange(ref _socket, null);

    /// <summary>The omc process, now this caller's alone to end; null when another took it first.</summary>
    private Process? TakeProcess() => Interlocked.Exchange(ref _omcProcess, null);

    /// <summary>
    /// Ends omc and everything it started. The one way this class ends omc, whether a command was
    /// given up on or the session is being disposed because MLQT is exiting (B493).
    /// </summary>
    private static void EndProcessTree(Process process) => process.Kill(entireProcessTree: true);

    /// <summary>
    /// Parses a response and removes outer quotes if present.
    /// OMC returns strings with quotes: "value" -> value
    /// </summary>
    private string UnquoteString(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }
        return trimmed;
    }

    /// <summary>
    /// Parses a boolean response from OMC.
    /// </summary>
    private bool ParseBoolean(string response)
    {
        var trimmed = response.Trim().ToLowerInvariant();
        return trimmed == "true";
    }

    // ========== OMC API Methods ==========

    /// <summary>
    /// Gets the OpenModelica version.
    /// </summary>
    public async Task<string> GetVersionAsync()
    {
        var response = await SendCommandAsync("getVersion()");
        return UnquoteString(response);
    }

    /// <summary>
    /// Loads a Modelica library by name.
    /// </summary>
    /// <param name="libraryName">Name of the library (e.g., "Modelica")</param>
    /// <param name="version">Optional version string (e.g., "4.0.0")</param>
    public async Task<bool> LoadModelAsync(string libraryName, string? version = null,
        CancellationToken cancellationToken = default)
    {
        var command = version != null
            ? $"loadModel({libraryName}, {{\"{version}\"}})"
            : $"loadModel({libraryName})";
        var response = await SendCommandAsync(command, cancellationToken);
        return ParseBoolean(response);
    }

    /// <summary>
    /// Loads a Modelica file.
    /// </summary>
    /// <param name="filePath">Path to .mo file</param>
    public async Task<bool> LoadFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // Escape backslashes for Windows paths
        var escapedPath = filePath.Replace("\\", "/");
        var response = await SendCommandAsync($"loadFile(\"{escapedPath}\")", cancellationToken);
        return ParseBoolean(response);
    }

    /// <summary>
    /// Checks a model for errors.
    /// </summary>
    /// <param name="modelName">Fully qualified model name</param>
    public async Task<bool> CheckModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync($"checkModel({modelName})", cancellationToken);
        return response.Contains("completed successfully.");
    }

    /// <summary>
    /// Instantiates a model (checks and flattens it).
    /// </summary>
    /// <param name="modelName">Fully qualified model name</param>
    public async Task<string> InstantiateModelAsync(string modelName)
    {
        var response = await SendCommandAsync($"instantiateModel({modelName})");
        return UnquoteString(response);
    }

    /// <summary>
    /// Gets a list of all loaded class names.
    /// </summary>
    public async Task<string[]> GetClassNamesAsync()
    {
        var response = await SendCommandAsync("getClassNames()");
        return ParseStringArray(response);
    }

    /// <summary>
    /// Gets components of a class.
    /// </summary>
    public async Task<string> GetComponentsAsync(string modelName)
    {
        var response = await SendCommandAsync($"getComponents({modelName})");
        return response;
    }

    /// <summary>
    /// Simulates a model.
    /// </summary>
    public async Task<SimulationResult> SimulateModelAsync(
        string modelName,
        double startTime = 0.0,
        double stopTime = 1.0,
        int numberOfIntervals = 500,
        double tolerance = 1e-6,
        string method = "dassl")
    {
        var command = BuildSimulateCommand(modelName, startTime, stopTime, numberOfIntervals, tolerance, method);
        var response = await SendCommandAsync(command);

        return ParseSimulationResult(response);
    }

    /// <summary>
    /// Builds the OMC <c>simulate(...)</c> command string. Modelica/OMC commands are
    /// culture-invariant: numeric values must use '.' as the decimal separator regardless
    /// of the host machine's locale. <see cref="FormattableString.Invariant"/> forces
    /// invariant-culture formatting of the interpolated doubles. Note this must be a single
    /// interpolated string (not <c>$"..." + $"..."</c>, which concatenates to a plain string
    /// and would lose the invariant formatting).
    /// </summary>
    internal static string BuildSimulateCommand(
        string modelName, double startTime, double stopTime, int numberOfIntervals,
        double tolerance, string method)
    {
        return FormattableString.Invariant(
            $"simulate({modelName}, startTime={startTime}, stopTime={stopTime}, numberOfIntervals={numberOfIntervals}, tolerance={tolerance}, method=\"{method}\")");
    }

    /// <summary>
    /// Builds/compiles a model without simulating it.
    /// </summary>
    public async Task<bool> BuildModelAsync(string modelName)
    {
        var response = await SendCommandAsync($"buildModel({modelName})");
        // buildModel returns an array with executable and xml file paths
        return !response.Contains("Error") && response.Contains("{");
    }

    /// <summary>
    /// Gets the last error message.
    /// </summary>
    public async Task<string> GetErrorStringAsync()
    {
        var response = await SendCommandAsync("getErrorString()");
        return UnquoteString(response);
    }

    /// <summary>
    /// Clears all loaded classes and resets OMC state.
    /// </summary>
    public async Task<bool> ClearAsync()
    {
        var response = await SendCommandAsync("clear()");
        return ParseBoolean(response);
    }

    /// <summary>
    /// Changes the current working directory.
    /// </summary>
    public async Task<bool> SetWorkingDirectoryAsync(string directory)
    {
        var escapedPath = directory.Replace("\\", "/");
        var response = await SendCommandAsync($"cd(\"{escapedPath}\")");
        var setDirectory = UnquoteString(response);
        if (escapedPath.EndsWith("/") && !setDirectory.EndsWith("/"))
            setDirectory += "/";
        return escapedPath.Trim()==setDirectory.Trim();
    }

    /// <summary>
    /// Gets the current working directory.
    /// </summary>
    public async Task<string> GetWorkingDirectoryAsync()
    {
        var response = await SendCommandAsync("cd(\"\")");
        return UnquoteString(response);
    }

    /// <summary>
    /// Lists classes within a package.
    /// </summary>
    public async Task<string[]> GetClassNamesInPackageAsync(string packageName)
    {
        var response = await SendCommandAsync($"getClassNames({packageName})");
        return ParseStringArray(response);
    }

    /// <summary>
    /// Gets information about a class.
    /// </summary>
    public async Task<string> GetClassInformationAsync(string className)
    {
        var response = await SendCommandAsync($"getClassInformation({className})");
        return response;
    }

    /// <summary>
    /// Gets the documentation string for a class.
    /// </summary>
    public async Task<string> GetClassCommentAsync(string className)
    {
        var response = await SendCommandAsync($"getClassComment({className})");
        return UnquoteString(response);
    }

    /// <summary>
    /// Exits OMC gracefully.
    /// </summary>
    public async Task ExitAsync()
    {
        if (IsConnected)
        {
            try
            {
                await SendCommandAsync("quit()");
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Parses a string array response from OMC.
    /// Format: {"item1","item2","item3"}
    /// </summary>
    private string[] ParseStringArray(string response)
    {
        var trimmed = response.Trim();
        if (trimmed == "{}")
        {
            return Array.Empty<string>();
        }

        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
        {
            var content = trimmed.Substring(1, trimmed.Length - 2);
            var items = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var escape = false;

            foreach (var c in content)
            {
                if (escape)
                {
                    current.Append(c);
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    items.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                if (c==' ')
                    continue;

                current.Append(c);
            }

            if (current.Length > 0)
            {
                items.Add(current.ToString());
            }

            return items.ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Parses simulation result from OMC response which is a Modelica record
    /// </summary>
    private SimulationResult ParseSimulationResult(string response)
    {
        SimulationResult result = new();
        result.Success = response.Contains("The simulation finished successfully.");

        var startIdx = response.IndexOf("resultFile = \"") + 14;
        var endIdx = response.IndexOf("\n", startIdx) - 2;
        result.ResultFile = response.Substring(startIdx, endIdx - startIdx);

        startIdx = response.IndexOf("messages = \"") + 12;
        endIdx = response.IndexOf("timeFrontend = ", startIdx);
        endIdx = response.LastIndexOf("\"", endIdx);
        result.Messages = response.Substring(startIdx, endIdx - startIdx);
        return result;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        // `quit()` first, and the flag afterwards: ExitAsync asks IsConnected, which is false once
        // _isDisposed is set, so setting it here meant the graceful exit was never actually sent and
        // omc was always killed instead. Killed, it leaves its temporary directory behind.
        var quitAnswered = false;
        try
        {
            quitAnswered = IsConnected && ExitAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore errors during shutdown
        }

        _isDisposed = true;

        // A command still in flight - one with no time limit, say, which quit() above could not get
        // past - is stopped, and the socket and lock are taken from it before they go. Disposing them
        // underneath it made its exchange fail on a disposed socket and its release throw on a
        // disposed lock (B369). A slice is 100 ms, so this wait is short.
        _lifetime.Cancel();
        var held = _commandLock.Wait(TimeSpan.FromSeconds(2));
        try
        {
            // Taken, not read: the lock is not always held here (the wait above can run out), and a
            // start being cancelled by _lifetime abandons the session without it. See Abandon.
            try { TakeSocket()?.Dispose(); } catch { /* already unusable */ }

            if (TakeProcess() is { } process)
            {
                try
                {
                    // An omc that answered quit() is on its way out and is given a moment to go by
                    // itself - it answers before it exits, so asking HasExited at once found it still
                    // there and killed it anyway, and the graceful exit above was graceful in name only.
                    if (!process.HasExited && !(quitAnswered && process.WaitForExit(2000)))
                    {
                        // The whole tree (B493). omc runs what a command asks for - a compiler, a
                        // simulation, a system() call - as children, and one busy enough not to answer
                        // quit() is busy with exactly that. Kill() ended omc and left the child: on
                        // Linux it is handed to init and runs on, headless, with nothing to say whose
                        // it was.
                        EndProcessTree(process);
                        process.WaitForExit(5000);
                    }
                }
                catch
                {
                    // Gone already, or not ours to end.
                }
                process.Dispose();
            }
        }
        finally
        {
            if (held)
                _commandLock.Release();
        }

        // The lock is not disposed: a caller that reached it just as this ran must be able to take
        // and release it, and learn from IsConnected that the session has gone. It holds nothing that
        // needs disposing unless its wait handle is asked for, which nothing here does.
    }
}
