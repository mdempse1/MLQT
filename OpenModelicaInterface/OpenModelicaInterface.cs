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
    private const int DefaultPort = 13027;
    private readonly int _port;

    /// <summary>
    /// Creates a new OpenModelica interface instance.
    /// </summary>
    /// <param name="omcPath">Path to omc.exe (e.g., "C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe")</param>
    /// <param name="port">ZMQ port to use (default: 13027)</param>
    public OpenModelicaInterface(string omcPath, int port = DefaultPort)
    {
        _omcPath = omcPath;
        _port = port;
    }

    /// <summary>
    /// Checks if OMC is connected via ZMQ.
    /// </summary>
    public bool IsConnected => _socket != null && !_isDisposed;

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
    public async Task StartAsync()
    {
        if (IsConnected)
        {
            return;
        }

        if (string.IsNullOrEmpty(_omcPath) || !File.Exists(_omcPath))
        {
            throw new FileNotFoundException($"OMC executable not found at: {_omcPath}");
        }

        // Start OMC with ZMQ server
        var startInfo = new ProcessStartInfo
        {
            FileName = _omcPath,
            Arguments = $"--interactive=zmq --interactivePort={_port}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _omcProcess = Process.Start(startInfo);
        if (_omcProcess == null)
        {
            throw new InvalidOperationException("Failed to start OMC process");
        }

        // Start background readers to consume stdout/stderr (prevent blocking)
        _ = Task.Run(() => ConsumeStreamAsync(_omcProcess.StandardOutput));
        _ = Task.Run(() => ConsumeStreamAsync(_omcProcess.StandardError));

        // Connect straight away: ZeroMQ keeps trying until omc has bound its port, and holds the
        // first request until then, so the question is only how long to wait for the answer.
        try
        {
            _socket = new RequestSocket();
            _socket.Connect($"tcp://127.0.0.1:{_port}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to connect to OMC on port {_port}", ex);
        }

        // Verify connection by getting version, within StartupTimeout.
        string version;
        try
        {
            version = UnquoteString(await SendCommandAsync("getVersion()", StartupTimeout, "start"));
        }
        catch (TimeoutException)
        {
            throw;   // already says what happened, and the session has been closed
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

    /// <summary>
    /// Consumes a stream asynchronously to prevent process blocking.
    /// </summary>
    private async Task ConsumeStreamAsync(StreamReader reader)
    {
        try
        {
            while (!_isDisposed && reader != null)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    // End of stream: omc has exited. Looping on here spun a thread at full speed for
                    // as long as this object lived, which a session closed after a timeout does.
                    break;
                }

                Debug.WriteLine($"OMC: {line}");
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected when disposing
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
    public Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
        => SendCommandAsync(command, CommandTimeout, "command", cancellationToken);

    private async Task<string> SendCommandAsync(
        string command, TimeSpan timeout, string what, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to OMC. Call StartAsync() first.");
        }

        // Cancelled while queued: nothing has been sent, so the session is untouched.
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            var socket = _socket!;

            // Sent and received against one clock, both in slices, so the wait can end on the time
            // limit or the token rather than only when omc answers. The send needs it as much as the
            // receive: a REQ socket with no peer blocks in SendFrame until one connects, so an omc
            // that never binds its port - or one still starting - held a bare send for as long as it
            // took, and a start-up limit on the receive alone bounded nothing.
            var response = await Task.Run(() => Exchange(socket, command, timeout, cancellationToken));
            if (response is null)
            {
                Abandon();
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(what == "start"
                    ? $"OpenModelica did not start and answer within {timeout.TotalSeconds:0.#}s; it has been stopped."
                    : $"OpenModelica did not answer {Describe(command)} within {timeout.TotalSeconds:0.#}s; the session has been closed.");
            }

            return response;
        }
        catch (Exception ex) when (ex is not TimeoutException and not OperationCanceledException)
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
    private static string? Exchange(
        RequestSocket socket, string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();

        var sent = false;
        while (!sent)
        {
            if (NextWait(timeout, clock, cancellationToken) is not { } wait)
                return null;
            sent = socket.TrySendFrame(wait, command);
        }

        while (true)
        {
            if (NextWait(timeout, clock, cancellationToken) is not { } wait)
                return null;
            if (socket.TryReceiveFrameString(wait, out var reply))
                return reply ?? "";
        }
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
    private void Abandon()
    {
        try { _socket?.Dispose(); } catch { /* already unusable */ }
        _socket = null;

        if (_omcProcess != null)
        {
            try
            {
                if (!_omcProcess.HasExited)
                    _omcProcess.Kill(entireProcessTree: true);
            }
            catch
            {
                // Gone already, or not ours to kill.
            }
            _omcProcess.Dispose();
            _omcProcess = null;
        }
    }

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
    public async Task<bool> LoadModelAsync(string libraryName, string? version = null)
    {
        var command = version != null
            ? $"loadModel({libraryName}, {{\"{version}\"}})"
            : $"loadModel({libraryName})";
        var response = await SendCommandAsync(command);
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
        try
        {
            ExitAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore errors during shutdown
        }

        _isDisposed = true;

        _socket?.Dispose();

        if (_omcProcess != null && !_omcProcess.HasExited)
        {
            try
            {
                _omcProcess.Kill();
                _omcProcess.WaitForExit(5000);
            }
            catch
            {
                // Ignore
            }
            _omcProcess.Dispose();
        }

        _commandLock.Dispose();
    }
}
