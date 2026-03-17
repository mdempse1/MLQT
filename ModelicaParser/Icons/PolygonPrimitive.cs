namespace ModelicaParser.Icons;

/// <summary>
/// Polygon graphics primitive.
/// </summary>
public class PolygonPrimitive : GraphicsPrimitive
{
    public override string Type => "Polygon";

    /// <summary>
    /// Points as array of [x, y] pairs.
    /// </summary>
    public List<double[]> Points { get; set; } = new();

    /// <summary>
    /// Smooth style ("None", "Bezier").
    /// </summary>
    public string Smooth { get; set; } = "None";
}
