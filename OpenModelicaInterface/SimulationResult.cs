namespace OpenModelicaInterface;

/// <summary>
/// Result of a simulation operation.
/// </summary>
public class SimulationResult
{
    public bool Success { get; set; }
    public string ResultFile { get; set; } = "";
    public string Messages { get; set; } = "";
}
