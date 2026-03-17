using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DymolaInterface;

/// <summary>
/// Class representing an instance of DymolaInterface for C#.
///
/// This class provides a C# API for accessing the most useful built-in functions in Dymola.
/// The API is compatible with Dymola 2025x Refresh 1.
///
/// In order to use the interface you need to run Dymola with the command-line option -serverport 8082.
///
/// Example usage:
/// <code>
/// try
/// {
///     var dymola = new DymolaInterface();
///     var result = await dymola.SimulateModelAsync("Modelica.Mechanics.Rotational.Examples.CoupledClutches");
///     if (result)
///     {
///         result = await dymola.PlotAsync(new[] { "J1.w", "J2.w", "J3.w", "J4.w" });
///         if (result)
///         {
///             result = await dymola.ExportPlotAsImageAsync("C:/temp/plot.png");
///         }
///     }
///     else
///     {
///         var log = await dymola.GetLastErrorLogAsync();
///         Console.Error.WriteLine(log);
///     }
/// }
/// catch (Exception e)
/// {
///     Console.Error.WriteLine($"Exception: {e.Message}");
/// }
/// </code>
/// </summary>
public class DymolaInterface : IDisposable
{
    private const double DymolaVersion = 2025.1;

    private readonly string _dymolaPath;
    private readonly int _portNumber;
    private readonly string _hostname;
    private readonly HttpClient _httpClient;
    private readonly string _dymolaUrl;
    private Process? _dymolaProcess;
    private int _rpcId;
    private bool _isOffline;
    private bool _disposed;
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    /// <summary>
    /// Create an instance of DymolaInterface.
    /// </summary>
    /// <param name="dymolaPath">Path to Dymola executable (optional if Dymola is already running).</param>
    /// <param name="portNumber">The port number to use for connecting to Dymola (default: 8082).</param>
    /// <param name="hostname">The hostname of the computer that runs Dymola (default: 127.0.0.1).</param>
    public DymolaInterface(string dymolaPath = "", int portNumber = 8082, string hostname = "127.0.0.1")
    {
        _dymolaPath = dymolaPath;
        _portNumber = portNumber;
        _hostname = hostname;
        _dymolaUrl = $"http://{hostname}:{portNumber}";
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) }; // 5 minute timeout
        _rpcId = 0;
        _isOffline = !IsDymolaRunning();
    }

    /// <summary>
    /// Starts the Dymola process if not already running.
    /// </summary>
    public async Task StartDymolaProcessAsync()
    {
        if (_dymolaProcess != null && !_dymolaProcess.HasExited)
            return;

        if (string.IsNullOrEmpty(_dymolaPath))
            throw new InvalidOperationException("Dymola path not specified.");

        await _commandLock.WaitAsync();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _dymolaPath,
                Arguments = $"-serverport {_portNumber}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _dymolaProcess = Process.Start(startInfo);

            // Wait for Dymola to start and become available
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);
                if (IsDymolaRunning())
                {
                    _isOffline = false;

                    return;
                }
            }

            throw new TimeoutException("Dymola did not start within the expected time.");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Stops the Dymola process if it was started by this interface.
    /// </summary>
    public async Task StopDymolaProcessAsync()
    {
        await _commandLock.WaitAsync();
        try {
            if (_dymolaProcess != null && !_dymolaProcess.HasExited)
            {
                _dymolaProcess.Kill();
                _dymolaProcess.WaitForExit();
                _dymolaProcess.Dispose();
                _dymolaProcess = null;
            }
            _isOffline = true;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private bool IsDymolaRunning()
    {
        try
        {
            _rpcId++;
            var request = new
            {
                method = "ping",
                @params = (object?)null,
                id = _rpcId
            };

            var jsonRequest = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = _httpClient.PostAsync(_dymolaUrl, content).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<JsonElement?> CallDymolaFunctionAsync(string cmd, object?[]? parameters = null)
    {
        if (_isOffline)
            return null;

        await _commandLock.WaitAsync();
        try
        {
            _rpcId++;
            var request = new
            {
                method = cmd.Replace("\\", "\\\\"),
                @params = parameters ?? Array.Empty<object>(),
                id = _rpcId
            };

            var jsonRequest = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_dymolaUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseText);

            if (jsonResponse.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Null)
            {
                if (jsonResponse.TryGetProperty("result", out var result))
                {
                    return result;
                }
            }
            else if (jsonResponse.TryGetProperty("error", out var errorObj))
            {
                throw new Exception($"Dymola error: {errorObj.ToString()}");
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Exception calling Dymola function: {e.Message}");
        }
        finally
        {
            _commandLock.Release();
        }

        return null;
    }

    /// <summary>
    /// Safely extracts a boolean result from a JsonElement.
    /// Handles cases where Dymola returns an empty object {} instead of a boolean.
    /// </summary>
    /// <param name="result">The JSON result from Dymola.</param>
    /// <returns>True if the result is true or an empty object, false otherwise.</returns>
    private bool GetBooleanResult(JsonElement? result)
    {
        if (result == null)
            return false;

        var element = result.Value;

        // Handle boolean results
        if (element.ValueKind == JsonValueKind.True)
            return true;
        if (element.ValueKind == JsonValueKind.False)
            return false;

        // Handle empty object {} as success (Dymola returns this for some commands)
        if (element.ValueKind == JsonValueKind.Object)
        {
            // Empty object is considered success
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the interface is in offline mode.
    /// </summary>
    public bool IsOfflineMode() => _isOffline;

    /// <summary>
    /// Sets the offline mode.
    /// </summary>
    /// <param name="enable">True to enable offline mode, false otherwise.</param>
    public void SetOfflineMode(bool enable)
    {
        _isOffline = enable;
    }

    /// <summary>
    /// Execute an arbitrary Dymola command.
    /// </summary>
    /// <param name="cmd">The command to execute.</param>
    public async Task<bool> ExecuteCommandAsync(string cmd)
    {
        var result = await CallDymolaFunctionAsync("ExecuteCommand", new object[] { cmd });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Get the last error message.
    /// </summary>
    /// <returns>The error message as a string.</returns>
    public async Task<string> GetLastErrorAsync()
    {
        var result = await CallDymolaFunctionAsync("getLastError");
        if (result?.ValueKind == JsonValueKind.Array)
        {
            var items = result.Value.EnumerateArray().Select(item => item.GetString() ?? string.Empty);
            return items.First().ToString() ?? "";
        }
        return "";
    }

    /// <summary>
    /// Get the Dymola version string.
    /// </summary>
    /// <returns>The version string.</returns>
    public async Task<string> DymolaVersionAsync()
    {
        var result = await CallDymolaFunctionAsync("DymolaVersion");
        if (result?.ValueKind == JsonValueKind.String)
        {
            return result.Value.GetString() ?? "";
        }
        return "";
    }

    /// <summary>
    /// Get the Dymola version number.
    /// </summary>
    /// <returns>The version number.</returns>
    public async Task<double> DymolaVersionNumberAsync()
    {
        var result = await CallDymolaFunctionAsync("DymolaVersionNumber");
        if (result?.ValueKind == JsonValueKind.Number)
        {
            return result.Value.GetDouble();
        }
        return 0.0;
    }

    /// <summary>
    /// Check a model without simulating it.
    /// </summary>
    /// <param name="problem">The name of the model to check.</param>
    /// <param name="simulate">If true, also simulate the model.</param>
    /// <param name="constraint">If true, check constraints.</param>
    /// <returns>True if the check was successful.</returns>
    public async Task<bool> CheckModelAsync(string problem, bool simulate = false, bool constraint = false)
    {
        var result = await CallDymolaFunctionAsync("checkModel", new object[] { MakeModelicaString(problem), simulate, constraint });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Simulate a Modelica model.
    /// </summary>
    /// <param name="problem">The name of the model to simulate.</param>
    /// <param name="startTime">Start time of simulation (default: 0.0).</param>
    /// <param name="stopTime">Stop time of simulation (default: 1.0).</param>
    /// <param name="numberOfIntervals">Number of output intervals (default: 0).</param>
    /// <param name="outputInterval">Output interval (default: 0.0).</param>
    /// <param name="method">Simulation method (default: "Dassl").</param>
    /// <param name="tolerance">Simulation tolerance (default: 0.0001).</param>
    /// <param name="fixedStepSize">Fixed step size (default: 0.0).</param>
    /// <param name="resultFile">Name of result file (default: "dsres").</param>
    /// <returns>True if the simulation was successful.</returns>
    public async Task<bool> SimulateModelAsync(
        string problem = "",
        double startTime = 0.0,
        double stopTime = 1.0,
        int numberOfIntervals = 0,
        double outputInterval = 0.0,
        string method = "Dassl",
        double tolerance = 0.0001,
        double fixedStepSize = 0.0,
        string resultFile = "dsres")
    {
        var result = await CallDymolaFunctionAsync("simulateModel", new object[]
        {
            MakeModelicaString(problem), startTime, stopTime, numberOfIntervals, outputInterval,
            MakeModelicaString(method), tolerance, fixedStepSize, CheckDymolaPathString(resultFile)
        });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Translate a Modelica model (compile without simulating).
    /// </summary>
    /// <param name="problem">The name of the model to translate.</param>
    /// <returns>True if translation was successful.</returns>
    public async Task<bool> TranslateModelAsync(string problem)
    {
        var result = await CallDymolaFunctionAsync("translateModel", new object[] { MakeModelicaString(problem) });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Open a Modelica library or package.
    /// </summary>
    /// <param name="path">Path to the library file.</param>
    /// <param name="mustRead">If true, the file must exist (default: true).</param>
    /// <param name="changeDirectory">If true, change to the library directory (default: true).</param>
    /// <returns>True if the library was opened successfully.</returns>
    public async Task<bool> OpenModelAsync(string path, bool mustRead = true, bool changeDirectory = true)
    {
        var result = await CallDymolaFunctionAsync(
                                "openModel", 
                                new object[] { 
                                    CheckDymolaPathString(path), 
                                    mustRead, 
                                    changeDirectory 
                                }
                            );
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Open a Modelica model from a file.
    /// </summary>
    /// <param name="modelName">Model to open.</param>
    /// <param name="path">File path to open (can be empty string).</param>
    /// <param name="version">Version to open (can be empty string)</param>
    /// <param name="newTab">Open in new tab, rather than currently active tab</param>
    /// <returns>True if opened successfully.</returns>
    public async Task<bool> OpenModelFileAsync(string modelName, string path="", string version="", bool newTab=false)
    {
        var result = await CallDymolaFunctionAsync(
                                "openModelFile", 
                                new object[] { 
                                    MakeModelicaString(modelName), 
                                    CheckDymolaPathString(path), 
                                    MakeModelicaString(version), 
                                    newTab 
                                }
                            );
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Clear all variables and models from Dymola.
    /// </summary>
    /// <param name="fast">If true, perform fast clear (default: false).</param>
    /// <returns>True if clear was successful.</returns>
    public async Task<bool> ClearAsync(bool fast = false)
    {
        var result = await CallDymolaFunctionAsync("clear", new object[] { fast });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Save the log to a file.
    /// </summary>
    /// <param name="logfile">Path to save the log file (default: "dymolalog.txt").</param>
    /// <returns>True if save was successful.</returns>
    public async Task<bool> SaveLogAsync(string logfile = "dymolalog.txt")
    {
        var result = await CallDymolaFunctionAsync("savelog", new object[] { CheckDymolaPathString(logfile) });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Exit Dymola.
    /// </summary>
    /// <param name="status">Exit status code (default: 0).</param>
    /// <returns>True if exit command was sent successfully.</returns>
    public async Task<bool> ExitAsync(int status = 0)
    {
        var result = await CallDymolaFunctionAsync("exit", new object[] { status });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Set a Dymola variable.
    /// </summary>
    /// <param name="name">Name of the variable.</param>
    /// <param name="value">Value to set.</param>
    /// <returns>True if the variable was set successfully.</returns>
    public async Task<bool> SetVariableAsync(string name, object value)
    {
        var result = await CallDymolaFunctionAsync("SetVariable", new object[] { MakeModelicaString(name), value });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Add a path to the Modelica library path.
    /// </summary>
    /// <param name="path">The path to add.</param>
    /// <param name="erase">If true, erase existing paths (default: false).</param>
    /// <returns>True if the path was added successfully.</returns>
    public async Task<bool> AddModelicaPathAsync(string path, bool erase = false)
    {
        var result = await CallDymolaFunctionAsync("AddModelicaPath", new object[] { CheckDymolaPathString(path), erase });
        return GetBooleanResult(result);
    }

    /// <summary>
    /// Change the current working directory.
    /// </summary>
    /// <param name="dir">The directory to change to (default: empty for home directory).</param>
    /// <returns>True if the directory was changed successfully.</returns>
    public async Task<bool> CdAsync(string dir = "")
    {
        var result = await CallDymolaFunctionAsync("cd", new object[] { CheckDymolaPathString(dir) });
        return GetBooleanResult(result);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopDymolaProcessAsync().GetAwaiter().GetResult();
            _httpClient?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private string CheckDymolaPathString(string path)
    {
        //Need to make sure dir includes double quotes and backslashes are escaped
        string escapedPath = path.Replace("\\", "\\\\");
        if (!escapedPath.StartsWith("\""))
        {
            escapedPath = "\"" + escapedPath + "\"";
        }
        return escapedPath;
    }

    private string MakeModelicaString(string str)
    {
        if (!str.StartsWith("\""))
        {
            str = "\"" + str + "\"";
        }
        return str;
    }

    private string[] MakeModelicaStringy(string[] strArray)
    {
        for (int i = 0; i < strArray.Length; i++)
        {
            strArray[i] = MakeModelicaString(strArray[i]);
        }
        return strArray;
    }
}
