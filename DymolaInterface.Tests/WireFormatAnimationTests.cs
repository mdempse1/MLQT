using System.Text.Json;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Wire-format tests for animation-related wrappers plus the final
/// <c>variables</c> method.
/// </summary>
public class WireFormatAnimationTests
{
    [Fact]
    public async Task AnimationPositionAsync_EmptyArray_UsesFillExpression()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.AnimationPositionAsync(Array.Empty<double>(), id: 1);
        Assert.Equal("animationPosition", h.Handler.LastRequest.Method);
        // Empty position → fill(0, 0) expression, emitted as a bare string.
        Assert.Equal(JsonValueKind.String, h.Handler.LastRequest.Param(0).ValueKind);
        Assert.Contains("fill", h.Handler.LastRequest.Param(0).GetString());
    }

    [Fact]
    public async Task AnimationPositionAsync_NonEmptyArray_IsSerialisedAsArray()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.AnimationPositionAsync(new[] { 0.0, 1.0, 2.0, 3.0 }, id: 0, maximized: true);
        Assert.Equal(JsonValueKind.Array, h.Handler.LastRequest.Param(0).ValueKind);
        Assert.True(h.Handler.LastRequest.Param(2).GetBoolean());
    }

    [Fact]
    public async Task AnimationSetupAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.AnimationSetupAsync();
        Assert.Equal("animationSetup", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExportAnimationAsync_DefaultArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportAnimationAsync("out.mp4");
        Assert.Equal("exportAnimation", h.Handler.LastRequest.Method);
        Assert.Equal(6, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task LoadAnimationAsync_DefaultArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.LoadAnimationAsync("in.mat");
        Assert.Equal("loadAnimation", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task RunAnimationAsync_DefaultArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.RunAnimationAsync();
        Assert.Equal("RunAnimation", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task Visualize3dModelAsync_PassesModelName()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.Visualize3dModelAsync("M");
        Assert.Equal("visualize3dModel", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task VariablesAsync_DefaultsToStarArray()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.VariablesAsync();
        Assert.Equal("variables", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.ParamCount);
        Assert.Equal(JsonValueKind.Array, h.Handler.LastRequest.Param(1).ValueKind);
    }
}
