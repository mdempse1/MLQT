namespace ModelicaParser.Icons;

/// <summary>
/// Represents the Icon annotation data extracted from a Modelica class.
/// Contains the coordinate system and list of graphics primitives.
/// </summary>
public class IconData
{
    /// <summary>
    /// The coordinate system extent (default is {{-100,-100},{100,100}}).
    /// </summary>
    public double[] CoordinateExtent { get; set; } = { -100, -100, 100, 100 };

    /// <summary>
    /// Whether to preserve aspect ratio (default true).
    /// </summary>
    public bool PreserveAspectRatio { get; set; } = true;

    /// <summary>
    /// Initial scale factor.
    /// </summary>
    public double InitialScale { get; set; } = 0.1;

    /// <summary>
    /// List of graphics primitives that make up the icon.
    /// </summary>
    public List<GraphicsPrimitive> Graphics { get; set; } = new();

    /// <summary>
    /// Gets whether this icon has any graphics content.
    /// </summary>
    public bool HasGraphics => Graphics.Count > 0;

    /// <summary>
    /// Creates a new IconData that combines this icon with a base layer.
    /// </summary>
    /// <param name="baseIcon">The base class icon.</param>
    /// <returns>A new IconData with merged graphics.</returns>
    public IconData WithBaseLayer(IconData? baseIcon)
    {
        if (baseIcon == null || !baseIcon.HasGraphics)
            return this;

        var merged = new IconData
        {
            CoordinateExtent = CoordinateExtent,
            PreserveAspectRatio = PreserveAspectRatio,
            InitialScale = InitialScale,
            Graphics = new List<GraphicsPrimitive>(baseIcon.Graphics)
        };
        merged.Graphics.AddRange(Graphics);
        return merged;
    }
}
