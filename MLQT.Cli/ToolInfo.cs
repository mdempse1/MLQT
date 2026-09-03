using System.Reflection;

namespace MLQT.Cli;

/// <summary>
/// What this tool calls itself, and where a reader can go to find out more.
///
/// <para>Both matter beyond the <c>--version</c> line: a SARIF document names the tool that produced
/// it, and a consumer uses that to tell one run's results from another's and to point a developer at
/// the rule that fired. The SARIF validator flags a driver carrying neither
/// (<c>SARIF2005</c>).</para>
/// </summary>
internal static class ToolInfo
{
    public const string Name = "mlqt";

    /// <summary>
    /// Where the tool is documented. Authored in the csproj (<c>PackageProjectUrl</c>) and passed
    /// through as assembly metadata, so the package and the reports it writes cannot name different
    /// places.
    /// </summary>
    public static string InformationUri { get; } =
        Metadata("ProjectUrl") ?? "https://github.com/mdempse1/MLQT";

    /// <summary>
    /// The full informational version, build metadata and all — what a bug report should quote.
    /// </summary>
    public static string Version { get; } =
        typeof(ToolInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(ToolInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>
    /// The version without build metadata: <c>0.1.0</c> from <c>0.1.0+9f2c1b7…</c>. SARIF's
    /// <c>semanticVersion</c> is defined as semver, and the suffix a build stamps on is not.
    /// </summary>
    public static string SemanticVersion { get; } = Version.Split('+')[0];

    private static string? Metadata(string key) =>
        typeof(ToolInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value
            ? value
            : null;
}
