namespace ModelicaParser.Icons;

/// <summary>
/// A base class's graphics mapped into a region of the derived class's coordinate system, as an
/// extends clause's <c>IconMap(extent=...)</c> / <c>DiagramMap(extent=...)</c> says (MLS 3.6 §18.6.3,
/// B420).
/// </summary>
/// <remarks>
/// <para>"The base class coordinate system (and contents) is mapped to the region specified by the
/// attributes in the same way as done for components": the base's extent goes onto the region, a
/// reversed region mirrors, and where the base preserves its aspect ratio the scale is the same on
/// both axes and the result is centred in the region. The arithmetic is <see cref="CoordinateMap"/>,
/// which also maps what else the base contributes - its components' placements and its connect
/// lines (B436) - so all of them land in the same place.</para>
///
/// <para><b>The primitives are rewritten, not wrapped in a transform.</b> An icon is a flat list of
/// primitives, and every consumer of it - the renderers, the bounds a diagram is framed by, the
/// <c>%name</c> substitution - reads that list.</para>
/// </remarks>
public static class GraphicsMapping
{
    /// <summary>
    /// <paramref name="graphics"/>, drawn in <paramref name="system"/>, as they are drawn once that
    /// system is mapped into <paramref name="region"/>.
    /// </summary>
    /// <param name="system">The base's resolved coordinate system: its extent and whether it
    /// preserves its aspect ratio.</param>
    /// <param name="region">The map's extent, [x1,y1,x2,y2] in the derived class's coordinates.</param>
    /// <returns>New primitives; the ones given are not changed. The graphics unchanged when the
    /// base's extent has no area, which maps onto nothing.</returns>
    public static List<GraphicsPrimitive> Into(
        IEnumerable<GraphicsPrimitive> graphics, IconData system, double[] region)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        return CoordinateMap.Into(system, region) is { } map
            ? [.. graphics.Select(map.Apply)]
            : [.. graphics];
    }
}
