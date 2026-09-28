using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace MLQT.McpTester.Services;

/// <summary>An image a tool returned, ready to put in an <c>img</c> tag.</summary>
/// <param name="DataUri">The whole <c>data:</c> URI, so the page needs no file and no handler.</param>
/// <param name="MimeType">What the server said it is.</param>
/// <param name="Bytes">Decoded size, for the caption — an image that came back empty is a real
/// failure mode and looks identical to one that is simply white.</param>
public sealed record ResultImage(string DataUri, string MimeType, int Bytes);

/// <summary>
/// One tool call's result, split into the part that is read and the part that is looked at.
///
/// <para>The tester used to print <c>[image content]</c> for anything that was not text, which is
/// the least useful thing it could say about a picture. MLQT's own <c>get_diagram_image</c> returns
/// a rendered diagram (B196) and the point of that tool is that a layout has to be <em>seen</em> —
/// but nothing here is MLQT-specific: any MCP server returning image content is shown the same way,
/// which is what this app is for.</para>
/// </summary>
public sealed record ToolResultView(string Text, IReadOnlyList<ResultImage> Images)
{
    /// <summary>Splits a call result into its text and its images.</summary>
    public static ToolResultView From(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = new StringBuilder();
        var images = new List<ResultImage>();

        foreach (var block in result.Content)
        {
            switch (block)
            {
                case TextContentBlock textBlock:
                    text.AppendLine(PrettyJson(textBlock.Text));
                    break;

                case ImageContentBlock image:
                    if (ToImage(image.Data, image.MimeType) is { } shown)
                        images.Add(shown);
                    else
                        text.AppendLine("[image content that could not be decoded]");
                    break;

                // An embedded resource can carry an image too, and a server that returns one means
                // the same thing by it. Anything else embedded stays text.
                case EmbeddedResourceBlock { Resource: BlobResourceContents blob }
                    when blob.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true:
                    if (ToImage(blob.Blob, blob.MimeType) is { } embedded)
                        images.Add(embedded);
                    else
                        text.AppendLine($"[{blob.MimeType} resource that could not be decoded]");
                    break;

                default:
                    text.AppendLine($"[{block.Type} content]");
                    break;
            }
        }

        if (result.StructuredContent is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } structured)
        {
            text.AppendLine();
            text.AppendLine("// structuredContent:");
            text.AppendLine(JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true }));
        }

        return new ToolResultView(text.ToString().Trim(), images);
    }

    /// <summary>
    /// The block's data as a <c>data:</c> URI. The wire carries base64 and the SDK hands it over as
    /// the UTF-8 bytes of that base64, so it goes into the URI as it is — decoding it only to
    /// re-encode it would be work to arrive back where we started. It <em>is</em> decoded once, to
    /// count the bytes and to find out whether it is base64 at all: a malformed payload must read as
    /// a broken result and not as a broken image icon.
    /// </summary>
    private static ResultImage? ToImage(ReadOnlyMemory<byte> data, string? mimeType)
    {
        var base64 = Encoding.UTF8.GetString(data.Span);
        if (base64.Length == 0)
            return null;

        Span<byte> decoded = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, decoded, out var bytes) || bytes == 0)
            return null;

        var type = string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType;
        return new ResultImage($"data:{type};base64,{base64}", type, bytes);
    }

    private static string PrettyJson(string text)
    {
        try
        {
            return JsonNode.Parse(text)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? text;
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
