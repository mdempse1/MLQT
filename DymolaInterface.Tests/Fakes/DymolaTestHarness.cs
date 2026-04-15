using System.Net.Http;
using System.Reflection;

namespace DymolaInterface.Tests.Fakes;

/// <summary>
/// Wraps a <see cref="DymolaInterface"/> instance with its <see cref="HttpClient"/>
/// replaced by a <see cref="FakeDymolaHttpHandler"/>, and flips the offline flag
/// so method wrappers actually serialise requests rather than short-circuiting.
///
/// Reflection is used because the target fields are private / readonly; no
/// production code changes are needed to make the interface testable.
/// </summary>
public sealed class DymolaTestHarness : IDisposable
{
    public FakeDymolaHttpHandler Handler { get; }
    public DymolaInterface Dymola { get; }

    public DymolaTestHarness()
    {
        Handler = new FakeDymolaHttpHandler();

        // Construct pointing at an unreachable port; IsDymolaRunning fails so the
        // instance starts in offline mode. We then swap the HttpClient and flip
        // the flag so further CallDymolaFunctionAsync() calls hit our handler.
        Dymola = new DymolaInterface(dymolaPath: string.Empty, portNumber: 1, hostname: "127.0.0.1");

        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(DymolaInterface);

        var clientField = type.GetField("_httpClient", flags)
            ?? throw new InvalidOperationException("_httpClient field not found");
        var offlineField = type.GetField("_isOffline", flags)
            ?? throw new InvalidOperationException("_isOffline field not found");

        var existing = (HttpClient?)clientField.GetValue(Dymola);
        existing?.Dispose();

        clientField.SetValue(Dymola, new HttpClient(Handler) { Timeout = TimeSpan.FromSeconds(30) });
        offlineField.SetValue(Dymola, false);

        // Clear any captures from the initial constructor ping (there won't be
        // any because construction was pointed at the unreachable port, but be
        // defensive in case the implementation changes).
        Handler.Clear();
    }

    /// <summary>
    /// Replace the handler's response with a success wrapper around a JSON
    /// fragment (e.g. "42" or "\"abc\"" or "[1,2,3]").
    /// </summary>
    public void SetResultJson(string jsonFragment)
    {
        Handler.ResponseBody = "{\"result\":" + jsonFragment + ",\"error\":null,\"id\":0}";
    }

    public void SetResultBool(bool value)
    {
        SetResultJson(value ? "true" : "false");
    }

    public void SetResultDouble(double value)
    {
        SetResultJson(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public void SetResultString(string value)
    {
        SetResultJson("\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
    }

    public void SetResultError(string message)
    {
        Handler.ResponseBody = "{\"result\":null,\"error\":\"" + message.Replace("\"", "\\\"") + "\",\"id\":0}";
    }

    public void Dispose()
    {
        Dymola.Dispose();
    }
}
