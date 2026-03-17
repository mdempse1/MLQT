namespace MLQT.Shared.Models;

public class DiagramNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Color { get; set; } = MudBlazor.Color.Primary.ToString();
    public string BorderColor { get; set; } = MudBlazor.Color.Primary.ToString();
}
