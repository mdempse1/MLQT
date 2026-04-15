using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DymolaInterface.Tests.Fakes;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> that captures every JSON-RPC request
/// that <see cref="DymolaInterface"/> emits and returns a configurable response.
///
/// Tests compose an instance, swap it into a <see cref="DymolaInterface"/> via
/// reflection (see <see cref="DymolaTestHarness"/>), invoke a wrapper method,
/// and then assert on <see cref="LastRequest"/> to verify the serialised form
/// matches Dymola's JSON-RPC expectations.
/// </summary>
public sealed class FakeDymolaHttpHandler : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = new();

    /// <summary>
    /// Response body to return for the next and subsequent requests. Tests can
    /// mutate this to simulate different Dymola responses (success booleans,
    /// numeric results, arrays, errors).
    /// </summary>
    public string ResponseBody { get; set; } = "{\"result\":true,\"error\":null,\"id\":1}";

    /// <summary>
    /// Status code returned to the client. Defaults to 200.
    /// </summary>
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public IReadOnlyList<CapturedRequest> Requests => _requests;

    public CapturedRequest LastRequest => _requests[^1];

    public void Clear() => _requests.Clear();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        string method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? string.Empty
            : string.Empty;

        JsonElement paramsClone = default;
        bool hasParams = root.TryGetProperty("params", out var p);
        if (hasParams) paramsClone = p.Clone();

        int id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
            ? idEl.GetInt32()
            : 0;

        _requests.Add(new CapturedRequest(method, paramsClone, body, id));

        return new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
        };
    }
}

public sealed record CapturedRequest(string Method, JsonElement Params, string RawBody, int Id)
{
    /// <summary>Number of top-level elements in the <c>params</c> array.</summary>
    public int ParamCount => Params.ValueKind == JsonValueKind.Array ? Params.GetArrayLength() : 0;

    /// <summary>Get the i-th param, or throw if out of range.</summary>
    public JsonElement Param(int index) => Params[index];

    /// <summary>Get a param as string (the raw JSON text of the element).</summary>
    public string ParamAsRawJson(int index) => Params[index].GetRawText();
}
