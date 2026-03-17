using Microsoft.JSInterop;
using System.Threading.Tasks;

namespace MLQT.Shared.Services;

/// <summary>
/// Service for retrieving browser window dimensions via JavaScript interop.
/// </summary>
public class BrowserService
{
    private readonly IJSRuntime _js;

    public BrowserService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<BrowserDimension> GetDimensionsAsync()
    {
        return await _js.InvokeAsync<BrowserDimension>("getDimensions");
    }

}

/// <summary>
/// Represents browser window dimensions.
/// </summary>
public class BrowserDimension
{
    public int Width { get; set; }
    public int Height { get; set; }
}