using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;

namespace MLQT.McpServer.Services;

/// <summary>
/// The serializer options the tools are registered with: the SDK's own defaults, with every result and
/// argument type MLQT defines taken from contracts compiled into this assembly.
/// </summary>
/// <remarks>
/// <para><b>Why compiled in.</b> A tool result's field names are what a client reads, and reflection
/// takes them from the record's members, which an obfuscated build renames. The source generator
/// writes them into the assembly when it is built, before any obfuscator runs (B502). The SDK's
/// resolver comes after this one, for its own protocol types.</para>
/// <para><b>Every type in <c>Dtos/</c> must be listed below</b>, and <c>ObfuscationReadinessTests</c> fails
/// for one that is not - otherwise it would quietly go to the SDK's resolver, which reflects. A tool returns
/// <c>object</c>, so the compiler cannot see which types reach the wire; a tool must return one of these
/// records, never an anonymous object, which a test also holds.</para>
/// </remarks>
internal static class McpServerJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                McpServerJsonContext.Default,
                McpJsonUtilities.DefaultOptions.TypeInfoResolver!),
        };
        options.MakeReadOnly();
        return options;
    }
}
