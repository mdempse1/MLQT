namespace ModelicaParser.StyleRules;

/// <summary>
/// Tracks counts of different section types within a Modelica class.
/// </summary>
internal class SectionTracker
{
    public int PublicSection { get; set; } = 0;
    public int ProtectedSection { get; set; } = 0;
    public int EquationSection { get; set; } = 0;
    public int InitialEquationSection { get; set; } = 0;
    public int AlgorithmSection { get; set; } = 0;
    public int InitialAlgorithmSection { get; set; } = 0;
}
