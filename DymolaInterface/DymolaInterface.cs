using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DymolaInterface;

// Enums (LinePattern, MarkerStyle, TextStyle, TextAlignment, SignalOperator)
// are defined in their own files in this namespace.

/// <summary>
/// Wraps a raw Modelica expression that should be passed through verbatim (without
/// string quoting). Mirrors the JavaScript <c>DymolaInterface.Expression</c> helper.
/// </summary>
public sealed class ModelicaExpression
{
    public string Value { get; }
    public ModelicaExpression(string value) { Value = value; }
}

/// <summary>
/// A Modelica named argument (e.g. <c>y={"PI.y"}</c>). Mirrors the JavaScript
/// <c>DymolaInterface.NamedArgument</c> helper; Dymola's JSON-RPC server accepts
/// named arguments when a <c>params</c> entry is serialized as <c>"name=value"</c>.
/// </summary>
public sealed class NamedArgument
{
    public string Name { get; }
    public object? Value { get; }
    public NamedArgument(string name, object? value) { Name = name; Value = value; }
}

/// <summary>
/// C# client for Dymola's JSON-RPC scripting API. Mirrors the JavaScript interface
/// shipped with Dymola (<c>Modelica/Library/javascript_interface/dymola_interface.js</c>).
/// </summary>
public class DymolaInterface : IDisposable
{
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

    public DymolaInterface(string dymolaPath = "", int portNumber = 8082, string hostname = "127.0.0.1")
    {
        _dymolaPath = dymolaPath;
        _portNumber = portNumber;
        _hostname = hostname;
        _dymolaUrl = $"http://{hostname}:{portNumber}";
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        _rpcId = 0;
        _isOffline = !IsDymolaRunning();
    }

    #region Process management

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

    public async Task StopDymolaProcessAsync()
    {
        await _commandLock.WaitAsync();
        try
        {
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
            var request = new { method = "ping", @params = (object?)null, id = _rpcId };
            var jsonRequest = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            var response = _httpClient.PostAsync(_dymolaUrl, content).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public bool IsOfflineMode() => _isOffline;
    public void SetOfflineMode(bool enable) => _isOffline = enable;

    public void Dispose()
    {
        if (_disposed) return;
        try { _dymolaProcess?.Kill(); _dymolaProcess?.Dispose(); } catch { /* ignore */ }
        _dymolaProcess = null;
        _httpClient.Dispose();
        _commandLock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Core JSON-RPC + parameter transformation

    /// <summary>
    /// Transform a C# parameter value into the form Dymola's JSON-RPC server expects.
    /// Mirrors <c>#fixJsonParameter</c> in the JS interface:
    ///   - string  → wrap with literal quote characters (<c>"value"</c>) so Dymola sees
    ///               a Modelica string literal;
    ///   - array   → recurse element-wise;
    ///   - NamedArgument → produce a <c>"name=value"</c> string;
    ///   - Expression    → raw verbatim string;
    ///   - anything else → pass through.
    /// </summary>
    private object? FixJsonParameter(object? item)
    {
        if (item is null) return null;

        switch (item)
        {
            case NamedArgument na: return FixNamedArgument(na);
            case ModelicaExpression e: return e.Value;
            case string s: return "\"" + EscapeModelicaString(s) + "\"";
            case bool b: return b;
            case Enum en: return Convert.ToInt32(en);
        }

        // Arrays: recurse over each element. Supports both string[] and object?[] (and
        // rank-2 arrays like double[][] used by writeMatrix / readMatrix).
        if (item is System.Collections.IEnumerable enumerable && item is not string)
        {
            var list = new List<object?>();
            foreach (var x in enumerable)
                list.Add(FixJsonParameter(x));
            return list.ToArray();
        }

        return item;
    }

    private object?[]? FixJsonParameterList(object?[]? parameters)
    {
        if (parameters is null) return null;
        var result = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
            result[i] = FixJsonParameter(parameters[i]);
        return result;
    }

    /// <summary>
    /// Build a <c>name=value</c> string for a NamedArgument. Array values are
    /// emitted as Modelica brace literals, e.g. <c>y={"PI.y","PI.u_s"}</c>.
    /// </summary>
    private string FixNamedArgument(NamedArgument named)
    {
        var fixedValue = FixJsonParameter(named.Value);
        string text;

        if (fixedValue is object?[] arr)
            text = FormatModelicaArray(arr);
        else if (fixedValue is null)
            text = "";
        else if (fixedValue is bool b)
            text = b ? "true" : "false";
        else
            text = fixedValue.ToString() ?? "";

        return named.Name + "=" + text;
    }

    private string FormatModelicaArray(object?[] arr)
    {
        var sb = new StringBuilder("{");
        for (int i = 0; i < arr.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var item = arr[i];
            if (item is object?[] inner)
                sb.Append(FormatModelicaArray(inner));
            else if (item is bool ib)
                sb.Append(ib ? "true" : "false");
            else
                sb.Append(item?.ToString() ?? "");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeModelicaString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    /// <summary>
    /// Issue a JSON-RPC call. Parameter transformation is applied via
    /// <see cref="FixJsonParameter"/> before serialization.
    /// </summary>
    protected async Task<JsonElement?> CallDymolaFunctionAsync(string cmd, object?[]? parameters = null)
    {
        if (_isOffline) return null;

        await _commandLock.WaitAsync();
        try
        {
            _rpcId++;
            var fixedParams = FixJsonParameterList(parameters) ?? Array.Empty<object?>();
            var request = new
            {
                method = cmd.Replace("\\", "\\\\"),
                @params = fixedParams,
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
                    return result;
            }
            else if (jsonResponse.TryGetProperty("error", out var errorObj))
            {
                throw new Exception($"Dymola error: {errorObj}");
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

    #endregion

    #region Result extraction helpers

    protected static bool GetBooleanResult(JsonElement? result)
    {
        if (result is null) return false;
        var e = result.Value;
        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => true, // Dymola returns {} for void-returning commands
            _ => false,
        };
    }

    protected static string GetStringResult(JsonElement? result)
    {
        if (result is null) return "";
        var e = result.Value;
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                return e.GetString() ?? "";
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        return item.GetString() ?? "";
                break;
            case JsonValueKind.Object:
                foreach (var prop in e.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        return prop.Value.GetString() ?? "";
                break;
        }
        return "";
    }

    protected static double GetDoubleResult(JsonElement? result)
    {
        if (result is null) return 0.0;
        var e = result.Value;
        if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
        if (e.ValueKind == JsonValueKind.Array)
            foreach (var item in e.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Number) return item.GetDouble();
        return 0.0;
    }

    protected static JsonElement? GetArrayResult(JsonElement? result)
    {
        if (result is null) return null;
        return result.Value.ValueKind == JsonValueKind.Array ? result : null;
    }

    #endregion

    #region Back-compat helpers (preserved from prior version)

    /// <summary>Used by callers that build Modelica strings manually.</summary>
    public static string MakeModelicaString(string str)
    {
        if (str.StartsWith("\"")) return str;
        return "\"" + str + "\"";
    }

    public static string CheckDymolaPathString(string path)
    {
        var p = path.Replace("\\", "/");
        if (!p.StartsWith("\"")) p = "\"" + p + "\"";
        return p;
    }

    public async Task<string> GetLastErrorAsync()
    {
        var result = await CallDymolaFunctionAsync("getLastError");
        return GetStringResult(result);
    }

    #endregion

    // ==========================================================================
    // JS-interface method mirrors. Every function below corresponds 1:1 with a
    // method in javascript_interface/dymola_interface.js. Signatures preserve
    // parameter order and defaults.
    // ==========================================================================

    #region Variables, commands, errors

    public async Task<bool> SetVariableAsync(string name, object value)
    {
        string s;
        switch (value)
        {
            case bool b: s = b ? "true" : "false"; break;
            case string str: s = "\"" + EscapeModelicaString(str) + "\""; break;
            case ModelicaExpression e: s = e.Value; break;
            case System.Collections.IEnumerable arr when value is not string:
                {
                    var items = new List<string>();
                    foreach (var item in arr)
                    {
                        if (item is string si) items.Add("\"" + EscapeModelicaString(si) + "\"");
                        else items.Add(item?.ToString() ?? "");
                    }
                    s = "{" + string.Join(",", items) + "}";
                    break;
                }
            default: s = value?.ToString() ?? ""; break;
        }
        return await ExecuteCommandAsync(name + "=" + s);
    }

    /// <summary>
    /// Execute a raw Modelica command. Unlike other methods, this sends the full
    /// command text as the JSON-RPC method name with no parameters, which is the
    /// form Dymola's scripting server evaluates verbatim.
    /// </summary>
    public async Task<bool> ExecuteCommandAsync(string cmd)
    {
        var result = await CallDymolaFunctionAsync(cmd, null);
        return GetBooleanResult(result);
    }

    public async Task<string> GetLastErrorLogAsync()
    {
        var arr = await CallDymolaFunctionAsync("getLastError");
        if (arr?.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.Value.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    return item.GetString() ?? "";
        }
        return "";
    }

    public async Task<JsonElement?> GetLastErrorRawAsync()
    {
        return await CallDymolaFunctionAsync("getLastError");
    }

    public async Task<bool> AddModelicaPathAsync(string path, bool erase = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("AddModelicaPath", new object?[] { path, erase }));

    public async Task<string> CdAsync(string dir = "")
    {
        var result = await CallDymolaFunctionAsync("cd", new object?[] { dir });
        return GetStringResult(result);
    }

    public async Task<bool> ClearAsync(bool fast = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("clear", new object?[] { fast }));

    public async Task<bool> ClearFlagsAsync()
        => GetBooleanResult(await CallDymolaFunctionAsync("clearFlags"));

    public async Task<bool> ClearLogAsync()
        => GetBooleanResult(await CallDymolaFunctionAsync("clearlog"));

    public async Task<bool> DefaultModelicaVersionAsync(string version, bool forceUpgrade)
        => GetBooleanResult(await CallDymolaFunctionAsync("DefaultModelicaVersion",
            new object?[] { version, forceUpgrade }));

    public async Task<bool> DocumentAsync(string function)
        => GetBooleanResult(await CallDymolaFunctionAsync("document", new object?[] { function }));

    public async Task<JsonElement?> DymolaLicenseInfoAsync()
        => await CallDymolaFunctionAsync("DymolaLicenseInfo");

    public async Task<string> DymolaVersionAsync()
        => GetStringResult(await CallDymolaFunctionAsync("DymolaVersion"));

    public async Task<double> DymolaVersionNumberAsync()
        => GetDoubleResult(await CallDymolaFunctionAsync("DymolaVersionNumber"));

    public async Task<bool> EraseClassesAsync(string[] classnames)
        => GetBooleanResult(await CallDymolaFunctionAsync("eraseClasses", new object?[] { classnames }));

    public async Task<bool> ExecuteAsync(string file, bool wait = true, bool terminal = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("Execute", new object?[] { file, wait, terminal }));

    public async Task<bool> ExitAsync(int status = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("exit", new object?[] { status }));

    public async Task<JsonElement?> GetDymolaCompilerAsync()
        => await CallDymolaFunctionAsync("GetDymolaCompiler");

    public async Task<string> GetRegistryValueAsync(string key, string name, bool convert = true, string defaultValue = "")
        => GetStringResult(await CallDymolaFunctionAsync("getRegistryValue",
            new object?[] { key, name, convert, defaultValue }));

    public async Task<bool> ListAsync(string filename = "", string[]? variables = null)
        => GetBooleanResult(await CallDymolaFunctionAsync("list",
            new object?[] { filename, variables ?? new[] { "*" } }));

    public async Task<bool> ListFunctionsAsync(string filter = "*", bool longForm = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("listfunctions", new object?[] { filter, longForm }));

    public async Task<bool> RequestOptionAsync(string optionName, string licenseDate = "", bool verbose = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("RequestOption",
            new object?[] { optionName, licenseDate, verbose }));

    public async Task<bool> RunScriptAsync(string script, bool silent = false, string scriptDir = "")
        => GetBooleanResult(await CallDymolaFunctionAsync("RunScript", new object?[] { script, silent, scriptDir }));

    public async Task<bool> SetDymolaCompilerAsync(string compiler, string[]? settings = null, bool mergeSettings = true)
        => GetBooleanResult(await CallDymolaFunctionAsync("SetDymolaCompiler",
            new object?[] { compiler, settings ?? new[] { "" }, mergeSettings }));

    public async Task<bool> ShowComponentAsync(string path, string[] components)
        => GetBooleanResult(await CallDymolaFunctionAsync("ShowComponent", new object?[] { path, components }));

    public async Task<bool> ShowMessageWindowAsync(bool show)
        => GetBooleanResult(await CallDymolaFunctionAsync("showMessageWindow", new object?[] { show }));

    public async Task<bool> SystemAsync(string command)
        => GetBooleanResult(await CallDymolaFunctionAsync("system", new object?[] { command }));

    public async Task<bool> TraceAsync(bool variables = false, bool statements = false, bool calls = false,
        string onlyFunction = "", bool profile = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("trace",
            new object?[] { variables, statements, calls, onlyFunction, profile }));

    public async Task<bool> VerifyCompilerAsync()
        => GetBooleanResult(await CallDymolaFunctionAsync("verifyCompiler"));

    #endregion

    #region Models / files / FMU

    public async Task<bool> CheckConversionAsync(string library, string fromVersion, string[] oldVersions,
        string reportPath, bool autoAddExtends)
        => GetBooleanResult(await CallDymolaFunctionAsync("checkConversion",
            new object?[] { library, fromVersion, oldVersions, reportPath, autoAddExtends }));

    public async Task<bool> CheckModelAsync(string problem, bool simulate = false, bool constraint = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("checkModel",
            new object?[] { problem, simulate, constraint }));

    public async Task<string> GetClassTextAsync(string fullName, bool includeAnnotations = false, bool formatted = false)
        => GetStringResult(await CallDymolaFunctionAsync("getClassText",
            new object?[] { fullName, includeAnnotations, formatted }));

    public async Task<JsonElement?> GetDependentLibrariesAsync(string modelName, bool dependentModels = false)
        => await CallDymolaFunctionAsync("getDependentLibraries", new object?[] { modelName, dependentModels });

    public async Task<bool> ImportFMUAsync(string fileName, bool includeAllVariables, bool integrate,
        bool promptReplacement, string packageName, bool includeVariables, string modelName, bool sourceCodeImport)
        => GetBooleanResult(await CallDymolaFunctionAsync("importFMU",
            new object?[] { fileName, includeAllVariables, integrate, promptReplacement, packageName,
                includeVariables, modelName, sourceCodeImport }));

    public async Task<bool> ImportInitialAsync(string dsName = "dsfinal.txt")
        => GetBooleanResult(await CallDymolaFunctionAsync("importInitial", new object?[] { dsName }));

    public async Task<bool> ImportInitialResultAsync(string dsResName, double atTime)
        => GetBooleanResult(await CallDymolaFunctionAsync("importInitialResult",
            new object?[] { dsResName, atTime }));

    public async Task<bool> ImportSSPAsync(string fileName, bool filterVariables = false,
        string packageName = "", bool silentSave = true, bool includeAllVariables = true)
        => GetBooleanResult(await CallDymolaFunctionAsync("importSSP",
            new object?[] { fileName, filterVariables, packageName, silentSave, includeAllVariables }));

    public async Task<bool> ImportSSVAsync(string fileName, string baseName = "", string packageName = "")
        => GetBooleanResult(await CallDymolaFunctionAsync("importSSV",
            new object?[] { fileName, baseName, packageName }));

    public async Task<bool> ExportSSPAsync(string modelName, string fileName = "")
        => GetBooleanResult(await CallDymolaFunctionAsync("exportSSP", new object?[] { modelName, fileName }));

    public async Task<bool> ExportDiagramAsync(string path, int width = 400, int height = 400,
        bool trim = true, string modelToExport = "", bool evaluate = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("exportDiagram",
            new object?[] { path, width, height, trim, modelToExport, evaluate }));

    public async Task<bool> ExportDocumentationAsync(string path, string className = "")
        => GetBooleanResult(await CallDymolaFunctionAsync("exportDocumentation", new object?[] { path, className }));

    public async Task<bool> ExportEquationsAsync(string path)
        => GetBooleanResult(await CallDymolaFunctionAsync("exportEquations", new object?[] { path }));

    public async Task<bool> ExportHTMLDirectoryAsync(string name, string directory = "",
        string[]? listOfLibraries = null, string[]? listOfLibraryDirectories = null, bool copyLinksLocally = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("exportHTMLDirectory",
            new object?[] { name, directory, listOfLibraries ?? new[] { name },
                listOfLibraryDirectories ?? new[] { "." }, copyLinksLocally }));

    public async Task<bool> ExportIconAsync(string path, int width = 80, int height = 80,
        bool trim = true, string modelToExport = "", bool evaluate = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("exportIcon",
            new object?[] { path, width, height, trim, modelToExport, evaluate }));

    public async Task<bool> ExportInitialAsync(string dsName, string scriptName,
        bool exportAllVariables, bool exportSimulator)
        => GetBooleanResult(await CallDymolaFunctionAsync("exportInitial",
            new object?[] { dsName, scriptName, exportAllVariables, exportSimulator }));

    public async Task<bool> ExportInitialDsinAsync(string scriptName)
        => GetBooleanResult(await CallDymolaFunctionAsync("exportInitialDsin", new object?[] { scriptName }));

    public async Task<bool> OpenModelAsync(string path, bool mustRead = true, bool changeDirectory = true)
        => GetBooleanResult(await CallDymolaFunctionAsync("openModel",
            new object?[] { path, mustRead, changeDirectory }));

    public async Task<bool> OpenModelFileAsync(string modelName, string path = "", string version = "", bool newTab = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("openModelFile",
            new object?[] { modelName, path, version, newTab }));

    public async Task<bool> SaveEncryptedModelAsync(string name, string path)
        => GetBooleanResult(await CallDymolaFunctionAsync("saveEncryptedModel", new object?[] { name, path }));

    public async Task<bool> SaveLogAsync(string logfile = "dymolalg.txt")
        => GetBooleanResult(await CallDymolaFunctionAsync("savelog", new object?[] { logfile }));

    public async Task<bool> SaveModelAsync(string name, string path, int storeVariant = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("saveModel",
            new object?[] { name, path, storeVariant }));

    public async Task<bool> SaveSettingsAsync(string fileName, bool storePlot = false, bool storeAnimation = false,
        bool storeSettings = false, bool storeVariables = false, bool storeInitial = true,
        bool storeAllVariables = true, bool storeSimulator = true, bool storePlotFilenames = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("saveSettings",
            new object?[] { fileName, storePlot, storeAnimation, storeSettings, storeVariables,
                storeInitial, storeAllVariables, storeSimulator, storePlotFilenames }));

    public async Task<bool> SaveTotalModelAsync(string fileName, string modelName,
        bool skipStandard = false, bool completePackage = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("saveTotalModel",
            new object?[] { fileName, modelName, skipStandard, completePackage }));

    public async Task<bool> SetClassTextAsync(string parentName, string fullText)
        => GetBooleanResult(await CallDymolaFunctionAsync("setClassText",
            new object?[] { parentName, fullText }));

    public async Task<bool> TranslateModelAsync(string problem)
        => GetBooleanResult(await CallDymolaFunctionAsync("translateModel", new object?[] { problem }));

    public async Task<bool> TranslateModelExportAsync(string modelName)
        => GetBooleanResult(await CallDymolaFunctionAsync("translateModelExport", new object?[] { modelName }));

    public async Task<bool> TranslateModelFMUAsync(string modelToOpen, bool storeResult, string modelName,
        string fmiVersion, string fmiType, bool includeSource, bool includeImage, bool includeVariables)
        => GetBooleanResult(await CallDymolaFunctionAsync("translateModelFMU",
            new object?[] { modelToOpen, storeResult, modelName, fmiVersion, fmiType,
                includeSource, includeImage, includeVariables }));

    #endregion

    #region Simulation / experiment / linearization

    public async Task<bool> ExperimentAsync(double startTime = 0.0, double stopTime = 1.0,
        int numberOfIntervals = 0, double outputInterval = 0.0, string algorithm = "",
        double tolerance = 0.0001, double fixedStepSize = 0.0)
        => GetBooleanResult(await CallDymolaFunctionAsync("experiment",
            new object?[] { startTime, stopTime, numberOfIntervals, outputInterval, algorithm,
                tolerance, fixedStepSize }));

    public async Task<JsonElement?> ExperimentGetOutputAsync()
        => await CallDymolaFunctionAsync("experimentGetOutput");

    public async Task<bool> ExperimentSetupOutputAsync(bool textual = false, bool doublePrecision = false,
        bool states = true, bool derivatives = true, bool inputs = true, bool outputs = true,
        bool auxiliaries = true, bool equidistant = true, bool events = true, bool onlyStopTime = false,
        bool debug = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("experimentSetupOutput",
            new object?[] { textual, doublePrecision, states, derivatives, inputs, outputs, auxiliaries,
                equidistant, events, onlyStopTime, debug }));

    public async Task<JsonElement?> GetExperimentAsync()
        => await CallDymolaFunctionAsync("getExperiment");

    public async Task<bool> InitializedAsync(bool allVars = false, bool isInitialized = true)
        => GetBooleanResult(await CallDymolaFunctionAsync("initialized",
            new object?[] { allVars, isInitialized }));

    public async Task<bool> LinearizeModelAsync(string problem = "", double startTime = 0.0,
        double stopTime = 1.0, int numberOfIntervals = 0, double outputInterval = 0.0,
        string method = "Dassl", double tolerance = 0.0001, double fixedStepSize = 0.0,
        string resultFile = "dslin")
        => GetBooleanResult(await CallDymolaFunctionAsync("linearizeModel",
            new object?[] { problem, startTime, stopTime, numberOfIntervals, outputInterval,
                method, tolerance, fixedStepSize, resultFile }));

    public async Task<bool> SimulateModelAsync(string problem = "", double startTime = 0.0,
        double stopTime = 1.0, int numberOfIntervals = 0, double outputInterval = 0.0,
        string method = "Dassl", double tolerance = 0.0001, double fixedStepSize = 0.0,
        string resultFile = "dsres")
        => GetBooleanResult(await CallDymolaFunctionAsync("simulateModel",
            new object?[] { problem, startTime, stopTime, numberOfIntervals, outputInterval,
                method, tolerance, fixedStepSize, resultFile }));

    public async Task<JsonElement?> SimulateExtendedModelAsync(string problem, double startTime,
        double stopTime, int numberOfIntervals, double outputInterval, string method, double tolerance,
        double fixedStepSize, string resultFile, string[] initialNames, double[] initialValues,
        string[] finalNames, bool autoLoad)
        => await CallDymolaFunctionAsync("simulateExtendedModel",
            new object?[] { problem, startTime, stopTime, numberOfIntervals, outputInterval, method,
                tolerance, fixedStepSize, resultFile, initialNames, initialValues, finalNames, autoLoad });

    public async Task<JsonElement?> SimulateMultiExtendedModelAsync(string problem, double startTime,
        double stopTime, int numberOfIntervals, double outputInterval, string method, double tolerance,
        double fixedStepSize, string resultFile, string[] initialNames, double[][] initialValues,
        string[] finalNames, string[] resultFileNames, bool autoLoad)
        => await CallDymolaFunctionAsync("simulateMultiExtendedModel",
            new object?[] { problem, startTime, stopTime, numberOfIntervals, outputInterval, method,
                tolerance, fixedStepSize, resultFile, initialNames, initialValues, finalNames,
                resultFileNames, autoLoad });

    public async Task<JsonElement?> SimulateMultiResultsModelAsync(string problem, double startTime,
        double stopTime, int numberOfIntervals, double outputInterval, string method, double tolerance,
        double fixedStepSize, string resultFile, string[] initialNames, double[][] initialValues,
        string[] resultNames, string[] resultFileNames, bool autoLoad)
        => await CallDymolaFunctionAsync("simulateMultiResultsModel",
            new object?[] { problem, startTime, stopTime, numberOfIntervals, outputInterval, method,
                tolerance, fixedStepSize, resultFile, initialNames, initialValues, resultNames,
                resultFileNames, autoLoad });

    #endregion

    #region Trajectories and result files

    public async Task<JsonElement?> ExistTrajectoryNamesAsync(string fileName, string[] names)
        => await CallDymolaFunctionAsync("existTrajectoryNames", new object?[] { fileName, names });

    public async Task<string> GetLastResultFileNameAsync()
        => GetStringResult(await CallDymolaFunctionAsync("Dymola_getLastResultFileName"));

    public async Task<JsonElement?> InterpolateTrajectoryAsync(string fileName, string[] signals, double[] times)
        => await CallDymolaFunctionAsync("interpolateTrajectory",
            new object?[] { fileName, signals, times });

    public async Task<JsonElement?> ReadMatrixAsync(string fileName, string matrixName, int rows, int columns)
        => await CallDymolaFunctionAsync("readMatrix",
            new object?[] { fileName, matrixName, rows, columns });

    public async Task<JsonElement?> ReadMatrixSizeAsync(string fileName, string matrixName)
        => await CallDymolaFunctionAsync("readMatrixSize", new object?[] { fileName, matrixName });

    public async Task<JsonElement?> ReadStringMatrixAsync(string fileName, string matrixName, int rows)
        => await CallDymolaFunctionAsync("readStringMatrix",
            new object?[] { fileName, matrixName, rows });

    public async Task<JsonElement?> ReadTrajectoryAsync(string fileName, string[] signals, int rows)
        => await CallDymolaFunctionAsync("readTrajectory", new object?[] { fileName, signals, rows });

    public async Task<JsonElement?> ReadTrajectoryNamesAsync(string fileName)
        => await CallDymolaFunctionAsync("readTrajectoryNames", new object?[] { fileName });

    public async Task<int> ReadTrajectorySizeAsync(string fileName)
    {
        var r = await CallDymolaFunctionAsync("readTrajectorySize", new object?[] { fileName });
        return (int)GetDoubleResult(r);
    }

    public async Task<bool> RemoveResultsAsync(string[] results)
        => GetBooleanResult(await CallDymolaFunctionAsync("removeResults", new object?[] { results }));

    public async Task<bool> WriteMatrixAsync(string fileName, string matrixName, double[][] matrix, bool append = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("writeMatrix",
            new object?[] { fileName, matrixName, matrix, append }));

    public async Task<bool> WriteTrajectoryAsync(string fileName, string[] signals, double[][] values)
        => GetBooleanResult(await CallDymolaFunctionAsync("writeTrajectory",
            new object?[] { fileName, signals, values }));

    public async Task<double> SignalOperatorValueAsync(string variablePath, SignalOperator signalOperator,
        double startTime = -1e100, double stopTime = 1e100)
        => GetDoubleResult(await CallDymolaFunctionAsync("signalOperatorValue",
            new object?[] { variablePath, (int)signalOperator, startTime, stopTime }));

    public async Task<int> CalculateNumberPrecisionAsync(double[] values)
    {
        var r = await CallDymolaFunctionAsync("calculateNumberPrecision",
            values.Length == 0 ? new object?[] { new ModelicaExpression("fill(0, 0)") } : new object?[] { values });
        return (int)GetDoubleResult(r);
    }

    #endregion

    #region Plotting

    /// <summary>
    /// Plot signals. Uses named arguments (via <see cref="NamedArgument"/>) which is
    /// the form Dymola's JSON-RPC server requires for <c>plot</c>'s signature.
    /// </summary>
    public async Task<bool> PlotAsync(string[] y, string[]? legends = null, bool? plotInAll = null,
        int[][]? colors = null, LinePattern[]? patterns = null, MarkerStyle[]? markers = null,
        double[]? thicknesses = null, int[]? axes = null)
    {
        if (y == null || y.Length == 0)
        {
            Console.Error.WriteLine("PlotAsync: at least one variable name is required.");
            return false;
        }

        var args = new List<object?> { new NamedArgument("y", y) };
        if (legends != null) args.Add(new NamedArgument("legends", legends));
        if (plotInAll.HasValue) args.Add(new NamedArgument("plotInAll", plotInAll.Value));
        if (colors != null) args.Add(new NamedArgument("colors", colors));
        if (patterns != null) args.Add(new NamedArgument("patterns", patterns.Select(p => (int)p).ToArray()));
        if (markers != null) args.Add(new NamedArgument("markers", markers.Select(m => (int)m).ToArray()));
        if (thicknesses != null) args.Add(new NamedArgument("thicknesses", thicknesses));
        if (axes != null) args.Add(new NamedArgument("axes", axes));

        return GetBooleanResult(await CallDymolaFunctionAsync("plot", args.ToArray()));
    }

    public async Task<bool> PlotArrayAsync(double[] x, double[] y, int style = 0, string legend = "",
        int id = 0, int[]? color = null, LinePattern pattern = LinePattern.Default,
        MarkerStyle marker = MarkerStyle.Default, double thickness = -1, bool erase = true,
        int axis = 1, string unit = "")
        => GetBooleanResult(await CallDymolaFunctionAsync("plotArray",
            new object?[] { x, y, style, legend, id, color ?? new[] { -1, -1, -1 },
                (int)pattern, (int)marker, thickness, erase, axis, unit }));

    public async Task<bool> PlotArraysAsync(double[] x, double[][] y, int style, string[] legend,
        int id, string title, int[][] colors, LinePattern[] patterns, MarkerStyle[] markers,
        double[] thicknesses, int[] axes, string[] units)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotArrays",
            new object?[] { x, y, style, legend, id, title, colors,
                patterns.Select(p => (int)p).ToArray(), markers.Select(m => (int)m).ToArray(),
                thicknesses, axes, units }));

    public async Task<bool> PlotDiscretizedAsync(string[] vars, string legend, string independent,
        int id, bool erase)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotDiscretized",
            new object?[] { vars, legend, independent, id, erase }));

    public async Task<bool> PlotDocumentationAsync(string doc = "", int id = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotDocumentation", new object?[] { doc, id }));

    public async Task<bool> PlotExpressionAsync(string mapFunction, bool eraseOld = false,
        string expressionName = "", int id = 0, int axis = 1, string unit = "")
        => GetBooleanResult(await CallDymolaFunctionAsync("plotExpression",
            new object?[] { new ModelicaExpression(mapFunction), eraseOld, expressionName, id, axis, unit }));

    public async Task<bool> PlotExternalAsync(string fileName)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotExternal", new object?[] { fileName }));

    public async Task<bool> PlotHeadingAsync(string textString, double fontSize, string fontName,
        int[] lineColor, TextStyle[] textStyle, TextAlignment horizontalAlignment, int id)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotHeading",
            new object?[] { textString, fontSize, fontName, lineColor,
                textStyle.Select(t => (int)t).ToArray(), (int)horizontalAlignment, id }));

    public async Task<bool> PlotMovingAverageAsync(string variablePath, double startTime, double stopTime,
        double intervalLength, int order = 0, int id = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotMovingAverage",
            new object?[] { variablePath, startTime, stopTime, intervalLength, order, id }));

    public async Task<bool> PlotParametricCurveAsync(double[] x, double[] y, double[] s,
        string xName = "", string yName = "", string sName = "", string legend = "", int id = 0,
        int[]? color = null, LinePattern pattern = LinePattern.Default,
        MarkerStyle marker = MarkerStyle.Default, double thickness = -1, bool labelWithS = false,
        bool erase = true, int axis = 1)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotParametricCurve",
            new object?[] { x, y, s, xName, yName, sName, legend, id, color ?? new[] { -1, -1, -1 },
                (int)pattern, (int)marker, thickness, labelWithS, erase, axis }));

    public async Task<bool> PlotParametricCurvesAsync(double[][] x, double[][] y, double[][] s,
        string xName, string yName, string sName, string[] legends, int id, int[][] colors,
        LinePattern[] patterns, MarkerStyle[] markers, double[] thicknesses, bool labelWithS, int[] axes)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotParametricCurves",
            new object?[] { x, y, s, xName, yName, sName, legends, id, colors,
                patterns.Select(p => (int)p).ToArray(), markers.Select(m => (int)m).ToArray(),
                thicknesses, labelWithS, axes }));

    public async Task<bool> PlotRowColumnLabelsAsync(string[] x, string[] y, int id)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotRowColumnLabels",
            new object?[] { x, y, id }));

    public async Task<bool> PlotScatterAsync(double[] x, double[] y, int[][] colors,
        MarkerStyle[] markers, string legend, int axis, string unit, bool erase, int id)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotScatter",
            new object?[] { x, y, colors, markers.Select(m => (int)m).ToArray(),
                legend, axis, unit, erase, id }));

    public async Task<bool> PlotSignalDifferenceAsync(string variablePath, double startTime = 0,
        double stopTime = 0, int axis = 1, int id = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotSignalDifference",
            new object?[] { variablePath, startTime, stopTime, axis, id }));

    public async Task<bool> PlotSignalOperatorAsync(string variablePath, SignalOperator signalOperator,
        double startTime, double stopTime, double period = 0.0, int id = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotSignalOperator",
            new object?[] { variablePath, (int)signalOperator, startTime, stopTime, period, id }));

    public async Task<bool> PlotSignalOperatorHarmonicAsync(string variablePath, SignalOperator signalOperator,
        double startTime, double stopTime, double period, double intervalLength, int window,
        int harmonicNo, int id = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotSignalOperatorHarmonic",
            new object?[] { variablePath, (int)signalOperator, startTime, stopTime, period,
                intervalLength, window, harmonicNo, id }));

    public async Task<bool> PlotTextAsync(double[] extent, string textString, double fontSize,
        string fontName, int[] lineColor, TextStyle[] textStyle, TextAlignment horizontalAlignment, int id)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotText",
            new object?[] { extent, textString, fontSize, fontName, lineColor,
                textStyle.Select(t => (int)t).ToArray(), (int)horizontalAlignment, id }));

    public async Task<bool> PlotTitleAsync(string title = "", int id = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotTitle", new object?[] { title, id }));

    public async Task<bool> PlotWindowSetupAsync(int window)
        => GetBooleanResult(await CallDymolaFunctionAsync("plotWindowSetup", new object?[] { window }));

    public async Task<bool> PrintPlotAsync(string[] y, string[]? legends = null, bool? plotInAll = null,
        int[][]? colors = null, LinePattern[]? patterns = null, MarkerStyle[]? markers = null,
        double[]? thicknesses = null, int[]? axes = null)
    {
        var args = new List<object?> { new NamedArgument("y", y) };
        if (legends != null) args.Add(new NamedArgument("legends", legends));
        if (plotInAll.HasValue) args.Add(new NamedArgument("plotInAll", plotInAll.Value));
        if (colors != null) args.Add(new NamedArgument("colors", colors));
        if (patterns != null) args.Add(new NamedArgument("patterns", patterns.Select(p => (int)p).ToArray()));
        if (markers != null) args.Add(new NamedArgument("markers", markers.Select(m => (int)m).ToArray()));
        if (thicknesses != null) args.Add(new NamedArgument("thicknesses", thicknesses));
        if (axes != null) args.Add(new NamedArgument("axes", axes));
        return GetBooleanResult(await CallDymolaFunctionAsync("printPlot", args.ToArray()));
    }

    public async Task<bool> PrintPlotArrayAsync(double[] x, double[] y, int style = 0, string legend = "",
        int id = 0, int[]? color = null, LinePattern pattern = LinePattern.Default,
        MarkerStyle marker = MarkerStyle.Default, double thickness = -1, bool erase = true,
        int axis = 1, string unit = "")
        => GetBooleanResult(await CallDymolaFunctionAsync("printPlotArray",
            new object?[] { x, y, style, legend, id, color ?? new[] { -1, -1, -1 },
                (int)pattern, (int)marker, thickness, erase, axis, unit }));

    public async Task<bool> PrintPlotArraysAsync(double[] x, double[][] y, int style, string[] legend,
        int id, string title, int[][] colors, LinePattern[] patterns, MarkerStyle[] markers,
        double[] thicknesses, int[] axes, string[] units)
        => GetBooleanResult(await CallDymolaFunctionAsync("printPlotArrays",
            new object?[] { x, y, style, legend, id, title, colors,
                patterns.Select(p => (int)p).ToArray(), markers.Select(m => (int)m).ToArray(),
                thicknesses, axes, units }));

    public async Task<bool> ClearPlotAsync(int id = 0)
        => GetBooleanResult(await CallDymolaFunctionAsync("clearPlot", new object?[] { id }));

    public async Task<bool> CreatePlotAsync(int id, int[] position, string[] x, string[] y,
        string heading, double[] range, bool erase, bool autoscale, bool autoerase, bool autoreplot,
        bool description, bool grid, bool color, bool online, bool legend, double timeWindow,
        string filename, int legendLocation, bool legendHorizontal, bool legendFrame, bool supressMarker,
        bool logX, bool logY, string[] legends, int[] subPlot, bool uniformScaling, int leftTitleType,
        string leftTitle, int bottomTitleType, string bottomTitle, int[][] colors, LinePattern[] patterns,
        MarkerStyle[] markers, double[] thicknesses, double[] range2, bool logY2, int rightTitleType,
        string rightTitle, int[] axes, string timeUnit, string[] displayUnits, bool showOriginal,
        bool showDifference, bool plotInAll)
        => GetBooleanResult(await CallDymolaFunctionAsync("createPlot",
            new object?[] { id, position, x, y, heading, range, erase, autoscale, autoerase, autoreplot,
                description, grid, color, online, legend, timeWindow, filename, legendLocation,
                legendHorizontal, legendFrame, supressMarker, logX, logY, legends, subPlot, uniformScaling,
                leftTitleType, leftTitle, bottomTitleType, bottomTitle, colors,
                patterns.Select(p => (int)p).ToArray(), markers.Select(m => (int)m).ToArray(),
                thicknesses, range2, logY2, rightTitleType, rightTitle, axes, timeUnit, displayUnits,
                showOriginal, showDifference, plotInAll }));

    public async Task<bool> CreateTableAsync(int id, int[] position, string[] x, string[] y,
        string heading, bool autoerase, bool autoreplot, string filename, string[] legends,
        string timeUnit, string[] displayUnits, bool showOriginal, bool showDifference)
        => GetBooleanResult(await CallDymolaFunctionAsync("createTable",
            new object?[] { id, position, x, y, heading, autoerase, autoreplot, filename, legends,
                timeUnit, displayUnits, showOriginal, showDifference }));

    public async Task<bool> RemovePlotsAsync(bool closeResults = true)
        => GetBooleanResult(await CallDymolaFunctionAsync("removePlots", new object?[] { closeResults }));

    public async Task<bool> ExportPlotAsImageAsync(string fileName, int id = -1,
        bool includeInLog = true, bool onlyActiveSubplot = true)
        => GetBooleanResult(await CallDymolaFunctionAsync("ExportPlotAsImage",
            new object?[] { fileName, id, includeInLog, onlyActiveSubplot }));

    #endregion

    #region Animation

    public async Task<bool> AnimationPositionAsync(double[] position, int id, bool maximized = false)
    {
        object? positionArg = position.Length == 0 ? new ModelicaExpression("fill(0, 0)") : position;
        return GetBooleanResult(await CallDymolaFunctionAsync("animationPosition",
            new object?[] { positionArg, id, maximized }));
    }

    public async Task<bool> AnimationSetupAsync()
        => GetBooleanResult(await CallDymolaFunctionAsync("animationSetup"));

    public async Task<bool> ExportAnimationAsync(string path, int width = -1, int height = -1,
        int frameRate = 20, int quality = 1, bool repeat = true)
        => GetBooleanResult(await CallDymolaFunctionAsync("exportAnimation",
            new object?[] { path, width, height, frameRate, quality, repeat }));

    public async Task<bool> LoadAnimationAsync(string fileName, int id = 0, bool together = false)
        => GetBooleanResult(await CallDymolaFunctionAsync("loadAnimation",
            new object?[] { fileName, id, together }));

    public async Task<bool> RunAnimationAsync(bool immediate = true, string loadFile = "",
        bool ensureAnimationWindow = false, bool eraseOld = true)
        => GetBooleanResult(await CallDymolaFunctionAsync("RunAnimation",
            new object?[] { immediate, loadFile, ensureAnimationWindow, eraseOld }));

    public async Task<bool> Visualize3dModelAsync(string problem)
        => GetBooleanResult(await CallDymolaFunctionAsync("visualize3dModel", new object?[] { problem }));

    public async Task<bool> VariablesAsync(string filename = "", string[]? variables = null)
        => GetBooleanResult(await CallDymolaFunctionAsync("variables",
            new object?[] { filename, variables ?? new[] { "*" } }));

    #endregion
}
