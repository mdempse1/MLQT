using System.Text.Json.Serialization;
using DymolaInterface;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using OpenModelicaInterface;

namespace MLQT.Services;

/// <summary>The generated contracts behind <see cref="MlqtServicesJson"/> and the files MLQT.Services
/// writes: each type, and every type it reaches.</summary>
[JsonSerializable(typeof(RepositorySettingsCollection))]
[JsonSerializable(typeof(DymolaSettings))]
[JsonSerializable(typeof(OpenModelicaSettings))]
[JsonSerializable(typeof(Baseline.BaselineFile))]
[JsonSerializable(typeof(FindingExport.ExportDocument))]
internal sealed partial class MlqtServicesJsonContext : JsonSerializerContext;
