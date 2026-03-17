namespace ModelicaParser.Icons;

/// <summary>
/// Text graphics primitive.
/// </summary>
public class TextPrimitive : GraphicsPrimitive
{
    public override string Type => "Text";

    /// <summary>
    /// Extent as [x1, y1, x2, y2].
    /// </summary>
    public double[] Extent { get; set; } = { -100, -100, 100, 100 };

    /// <summary>
    /// The text string to display.
    /// </summary>
    public string TextString { get; set; } = "";

    /// <summary>
    /// Font size.
    /// </summary>
    public double FontSize { get; set; } = 0;

    /// <summary>
    /// Font name.
    /// </summary>
    public string FontName { get; set; } = "";

    /// <summary>
    /// Font styles (e.g., "Bold", "Italic", "Underline").
    /// </summary>
    public List<string> FontStyles { get; set; } = new();

    /// <summary>
    /// Horizontal alignment ("Left", "Center", "Right").
    /// </summary>
    public string HorizontalAlignment { get; set; } = "Center";

    /// <summary>
    /// Text color as RGB array.
    /// </summary>
    public int[] TextColor { get; set; } = { 0, 0, 0 };
}
