using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Maps a connection endpoint to the conventional Modelica line colour for its connector's domain, so an
/// auto-drawn connection looks like a hand-authored one (signal lines dark blue, electrical blue, thermal
/// red, …). Best-effort and heuristic: the port's connector type is resolved through the graph and matched
/// by name; when it cannot be resolved (e.g. the defining library is not loaded) the result is null and
/// the caller draws the line in the default colour.
/// </summary>
internal static class ConnectorColor
{
    // Ordered longest/most-specific first; the first substring found in the connector's id wins.
    private static readonly (string Marker, string Color)[] DomainColors =
    {
        ("RealInput", "{0,0,127}"), ("RealOutput", "{0,0,127}"), ("RealSignal", "{0,0,127}"),
        ("BooleanInput", "{255,0,255}"), ("BooleanOutput", "{255,0,255}"), ("BooleanSignal", "{255,0,255}"),
        ("IntegerInput", "{255,127,0}"), ("IntegerOutput", "{255,127,0}"), ("IntegerSignal", "{255,127,0}"),
        ("Electrical.Digital", "{127,0,127}"),
        ("Electrical", "{0,0,255}"), ("Pin", "{0,0,255}"), ("Plug", "{0,0,255}"),
        ("Translational", "{0,127,0}"),
        ("Rotational", "{95,95,95}"),
        ("MultiBody", "{95,95,95}"), ("Frame", "{95,95,95}"),
        ("HeatPort", "{191,0,0}"), ("Thermal", "{191,0,0}"),
        ("Fluid", "{0,127,255}"),
    };

    public static string? Resolve(ILibraryDataService libraries, string classId, string portRef)
    {
        if (ConnectorCompatibility.ResolvePort(libraries, classId, portRef).Connector is not { } connector)
            return null;

        foreach (var (marker, color) in DomainColors)
            if (connector.Id.Contains(marker, StringComparison.Ordinal))
                return color;
        return null;
    }
}
