namespace ModelicaParser.Icons;

/// <summary>
/// Bitmap graphics primitive.
/// </summary>
public class BitmapPrimitive : GraphicsPrimitive
{
    public override string Type => "Bitmap";

    /// <summary>
    /// Extent as [x1, y1, x2, y2].
    /// </summary>
    public double[] Extent { get; set; } = { -100, -100, 100, 100 };

    /// <summary>
    /// Image file name or URI.
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// Base64 encoded image data.
    /// </summary>
    public string ImageSource { get; set; } = "";
}
