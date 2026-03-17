namespace ModelicaParser.Icons;

/// <summary>
/// Ellipse graphics primitive.
/// </summary>
public class EllipsePrimitive : GraphicsPrimitive
{
    public override string Type => "Ellipse";

    /// <summary>
    /// Extent as [x1, y1, x2, y2].
    /// </summary>
    public double[] Extent { get; set; } = { -100, -100, 100, 100 };

    /// <summary>
    /// Start angle in degrees.
    /// </summary>
    public double StartAngle { get; set; } = 0;

    /// <summary>
    /// End angle in degrees.
    /// </summary>
    public double EndAngle { get; set; } = 360;

    /// <summary>
    /// Closure type (e.g., "None", "Chord", "Radial").
    /// </summary>
    public string Closure { get; set; } = "None";
}
