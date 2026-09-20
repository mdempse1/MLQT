using System.Buffers;
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

    /// <summary>
    /// How long to hold each request before answering, to stand in for a slow Dymola. The
    /// wait honours the request's cancellation, as a real network call does.
    /// </summary>
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Answer with this id instead of the request's own, standing in for the late reply to a
    /// command the caller already gave up on. Null - the default - echoes the request's id,
    /// which is what a real Dymola does.
    /// </summary>
    public int? ResponseIdOverride { get; set; }

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

        if (ResponseDelay > TimeSpan.Zero)
            await Task.Delay(ResponseDelay, cancellationToken);

        return new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(WithId(ResponseBody, ResponseIdOverride ?? id), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Stamp the answer with <paramref name="id"/>, as Dymola does: the canned bodies tests set
    /// carry a fixed id, and the interface discards a reply whose id is not the one it sent.
    /// A body that is not a JSON object is passed through untouched.
    /// </summary>
    private static string WithId(string body, int id)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return body;

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("id"))
                        continue;
                    property.WriteTo(writer);
                }
                writer.WriteNumber("id", id);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        catch (JsonException)
        {
            return body;
        }
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
