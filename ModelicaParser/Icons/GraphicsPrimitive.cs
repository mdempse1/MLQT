namespace ModelicaParser.Icons;

/// <summary>
/// Base class for all Modelica graphics primitives.
/// </summary>
public abstract class GraphicsPrimitive
{
    /// <summary>
    /// The type of graphics primitive.
    /// </summary>
    public abstract string Type { get; }

    /// <summary>
    /// Visibility of the primitive.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Origin point for transformations.
    /// </summary>
    public double[] Origin { get; set; } = { 0, 0 };

    /// <summary>
    /// Rotation angle in degrees.
    /// </summary>
    public double Rotation { get; set; } = 0;

    /// <summary>
    /// Line color as RGB array.
    /// </summary>
    public int[] LineColor { get; set; } = { 0, 0, 0 };

    /// <summary>
    /// Fill color as RGB array.
    /// </summary>
    public int[] FillColor { get; set; } = { 0, 0, 0 };

    /// <summary>
    /// Fill pattern (e.g., "None", "Solid", "Horizontal", "Vertical", etc.).
    /// </summary>
    public string FillPattern { get; set; } = "None";

    /// <summary>
    /// Line pattern (e.g., "None", "Solid", "Dash", "Dot", etc.).
    /// </summary>
    public string LinePattern { get; set; } = "Solid";

    /// <summary>
    /// Line thickness.
    /// </summary>
    public double LineThickness { get; set; } = 0.25;
}
