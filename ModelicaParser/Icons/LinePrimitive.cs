namespace ModelicaParser.Icons;

/// <summary>
/// Line graphics primitive.
/// </summary>
public class LinePrimitive : GraphicsPrimitive
{
    public override string Type => "Line";

    /// <summary>
    /// Points as array of [x, y] pairs.
    /// </summary>
    public List<double[]> Points { get; set; } = new();

    /// <summary>
    /// Arrow style at start ("None", "Open", "Filled", "Half").
    /// </summary>
    public string ArrowStart { get; set; } = "None";

    /// <summary>
    /// Arrow style at end ("None", "Open", "Filled", "Half").
    /// </summary>
    public string ArrowEnd { get; set; } = "None";

    /// <summary>
    /// Arrow size.
    /// </summary>
    public double ArrowSize { get; set; } = 3;

    /// <summary>
    /// Smooth style ("None", "Bezier").
    /// </summary>
    public string Smooth { get; set; } = "None";
}
