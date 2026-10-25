namespace MLQT.Shared.Models;

public class DiagramNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string FullName { get; set; } = "";
    // CSS colours, which cytoscapeGraph.js resolves when they are var(...). MudBlazor.Color.Primary's
    // name was the default once, and "Primary" is not a colour, so a node left at it could not be
    // painted (B512).
    public string Color { get; set; } = "var(--mud-palette-primary)";
    public string BorderColor { get; set; } = "var(--mud-palette-primary-darken)";
}
