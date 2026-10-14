using System.Text.Json.Serialization;
using ModelicaGraph.Analysis;

namespace ModelicaGraph;

/// <summary>The generated contracts behind <see cref="ModelicaGraphJson"/>: each type written to a file,
/// and every type it reaches.</summary>
[JsonSerializable(typeof(StyleCheckingSettings))]
[JsonSerializable(typeof(List<MetricsSnapshot>))]
internal sealed partial class ModelicaGraphJsonContext : JsonSerializerContext;
