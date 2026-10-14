using System.Text.Json.Serialization.Metadata;

namespace ModelicaGraph;

/// <summary>
/// The JSON contracts of ModelicaGraph's types that are written to files: <c>.mlqt/settings.json</c>
/// and the metrics history.
/// </summary>
/// <remarks>
/// <b>Compiled in, not read by reflection.</b> The source generator writes each property's name and
/// each constructor call into the assembly when it is built, before an obfuscator can rename
/// anything, so a build protected at the strongest settings still reads and writes the files every
/// other MLQT surface does (B502). Pass <see cref="Contracts"/> as the options' only resolver: a type
/// missing from it then fails loudly rather than falling back to reflection and its renamed names.
/// </remarks>
public static class ModelicaGraphJson
{
    /// <summary>The compiled contracts, as a resolver for <c>JsonSerializerOptions.TypeInfoResolver</c>.</summary>
    public static IJsonTypeInfoResolver Contracts => ModelicaGraphJsonContext.Default;
}
