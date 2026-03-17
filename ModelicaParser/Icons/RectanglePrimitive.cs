namespace ModelicaParser.Icons;

/// <summary>
/// Rectangle graphics primitive.
/// </summary>
public class RectanglePrimitive : GraphicsPrimitive
{
    public override string Type => "Rectangle";

    /// <summary>
    /// Border pattern (e.g., "None", "Raised", "Sunken", "Engraved").
    /// </summary>
    public string BorderPattern { get; set; } = "None";

    /// <summary>
    /// Extent as [x1, y1, x2, y2].
    /// </summary>
    public double[] Extent { get; set; } = { -100, -100, 100, 100 };

    /// <summary>
    /// Corner radius for rounded rectangles.
    /// </summary>
    public double Radius { get; set; } = 0;
}
