using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NetMQ;
using NetMQ.Sockets;

namespace OpenModelicaInterface;

/// <summary>
/// Interface to OpenModelica Compiler (OMC) using ZeroMQ (ZMQ) communication.
/// This class provides a C# wrapper around OMC's scripting API.
/// Based on OMPython's approach using ZMQ REQ-REP pattern.
/// </summary>
public class OpenModelicaInterface : IDisposable
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

        // Wait for OMC to start its ZMQ server
        await Task.Delay(2000);

        // Connect to OMC via ZMQ
        try
        {
            _socket = new RequestSocket();
            _socket.Connect($"tcp://127.0.0.1:{_port}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to connect to OMC on port {_port}", ex);
        }

        // Verify connection by getting version
        try
        {
            var version = await GetVersionAsync();
            if (string.IsNullOrEmpty(version))
            {
                throw new InvalidOperationException("Failed to establish communication with OMC");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("OMC started but failed to respond to commands", ex);
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
                if (line != null)
                {
                    Debug.WriteLine($"OMC: {line}");
                }
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
    public async Task<string> SendCommandAsync(string command)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to OMC. Call StartAsync() first.");
        }

        await _commandLock.WaitAsync();
        try
        {
            // Send command via ZMQ
            await Task.Run(() => _socket!.SendFrame(command));

            // Receive response via ZMQ (message boundary handled by ZMQ)
            var response = await Task.Run(() => _socket!.ReceiveFrameString());

            return response ?? "";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to send command to OMC: {command}", ex);
        }
        finally
        {
            _commandLock.Release();
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
    public async Task<bool> LoadFileAsync(string filePath)
    {
        // Escape backslashes for Windows paths
        var escapedPath = filePath.Replace("\\", "/");
        var response = await SendCommandAsync($"loadFile(\"{escapedPath}\")");
        return ParseBoolean(response);
    }

    /// <summary>
    /// Checks a model for errors.
    /// </summary>
    /// <param name="modelName">Fully qualified model name</param>
    public async Task<bool> CheckModelAsync(string modelName)
    {
        var response = await SendCommandAsync($"checkModel({modelName})");
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
        var command = $"simulate({modelName}, startTime={startTime}, stopTime={stopTime}, " +
                     $"numberOfIntervals={numberOfIntervals}, tolerance={tolerance}, method=\"{method}\")";
        var response = await SendCommandAsync(command);

        return ParseSimulationResult(response);
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

        _isDisposed = true;

        try
        {
            ExitAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore errors during shutdown
        }

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
