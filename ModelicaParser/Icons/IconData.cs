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
    /// Whether the annotation stated its coordinate system's extent, rather than leaving the default.
    /// A class that does not state one inherits its base's, which is a question only this answers:
    /// a stated {{-100,-100},{100,100}} and the default look the same (B316).
    /// </summary>
    public bool DeclaresExtent { get; set; }

    /// <summary>
    /// Whether to preserve aspect ratio (default true).
    /// </summary>
    public bool PreserveAspectRatio { get; set; } = true;

    /// <summary>Whether the annotation stated <see cref="PreserveAspectRatio"/>; see <see cref="DeclaresExtent"/>.</summary>
    public bool DeclaresPreserveAspectRatio { get; set; }

    /// <summary>
    /// Initial scale factor.
    /// </summary>
    public double InitialScale { get; set; } = 0.1;

    /// <summary>Whether the annotation stated <see cref="InitialScale"/>; see <see cref="DeclaresExtent"/>.</summary>
    public bool DeclaresInitialScale { get; set; }

    /// <summary>
    /// The coordinate system a class uses on one layer, by the Modelica specification's rule
    /// (MLS 3.6 §18.6.1.1): "The coordinate system attributes (extent and preserveAspectRatio) of a
    /// class are separately defined by the following priority: 1. The coordinate system annotation
    /// given in the class (if specified). 2. The coordinate systems of the first base class where
    /// the extent on the extends-clause specifies a null-region (if any). 3. The default coordinate
    /// system." (B394)
    ///
    /// <para><b>Separately</b> is the word that matters: a class stating only
    /// <c>coordinateSystem(preserveAspectRatio=false)</c> - 27 MSL classes do - still takes its
    /// extent from its base. <c>initialScale</c> is not named by the rule; it is resolved the same
    /// way, which is what a tool placing a new instance needs.</para>
    /// </summary>
    /// <param name="own">The class's own layer annotation, or null when it has none.</param>
    /// <param name="inherited">The <em>resolved</em> coordinate system of the first base whose
    /// extends clause maps it to a null region, or null when there is no such base.</param>
    /// <returns>The resolved system, with no graphics. Each <c>Declares</c> flag says whether the
    /// value was stated in this class or in the base it came from, rather than defaulted.</returns>
    public static IconData ResolveCoordinateSystem(IconData? own, IconData? inherited)
    {
        var fallback = inherited ?? new IconData();
        var extentFrom = own is { DeclaresExtent: true } ? own : fallback;
        var aspectFrom = own is { DeclaresPreserveAspectRatio: true } ? own : fallback;
        var scaleFrom = own is { DeclaresInitialScale: true } ? own : fallback;

        return new IconData
        {
            CoordinateExtent = extentFrom.CoordinateExtent,
            DeclaresExtent = extentFrom.DeclaresExtent,
            PreserveAspectRatio = aspectFrom.PreserveAspectRatio,
            DeclaresPreserveAspectRatio = aspectFrom.DeclaresPreserveAspectRatio,
            InitialScale = scaleFrom.InitialScale,
            DeclaresInitialScale = scaleFrom.DeclaresInitialScale,
        };
    }

    /// <summary>This coordinate system with <paramref name="graphics"/> drawn in it.</summary>
    public IconData WithGraphics(List<GraphicsPrimitive> graphics) => new()
    {
        CoordinateExtent = CoordinateExtent,
        DeclaresExtent = DeclaresExtent,
        PreserveAspectRatio = PreserveAspectRatio,
        DeclaresPreserveAspectRatio = DeclaresPreserveAspectRatio,
        InitialScale = InitialScale,
        DeclaresInitialScale = DeclaresInitialScale,
        Graphics = graphics,
    };

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
