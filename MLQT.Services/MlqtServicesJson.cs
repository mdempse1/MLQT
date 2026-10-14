using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MLQT.Services;

/// <summary>
/// The JSON contracts of MLQT.Services' types that are written to files: the baseline, and the
/// repository list a settings store keeps.
/// </summary>
/// <remarks>
/// <b>Compiled in, not read by reflection</b>, for the reason <c>ModelicaGraphJson</c> gives: the
/// source generator fixes each name and constructor call when the assembly is built, so an obfuscated
/// build reads and writes what every other MLQT build does (B502).
/// </remarks>
public static class MlqtServicesJson
{
    /// <summary>
    /// The options a settings store reads and writes one setting's value with. The stack's own setting
    /// types - the repository list, and the Dymola and OpenModelica settings - are compiled in; a type a
    /// host defines for its own settings (the desktop app's display settings, say) falls back to
    /// reflection, which is the host's business. Read-only.
    /// </summary>
    public static JsonSerializerOptions SettingValues { get; } = CreateSettingValues();

    private static JsonSerializerOptions CreateSettingValues()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(MlqtServicesJsonContext.Default, new DefaultJsonTypeInfoResolver()),
        };
        options.MakeReadOnly();
        return options;
    }
}
