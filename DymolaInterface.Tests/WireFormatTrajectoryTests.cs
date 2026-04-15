using System.Text.Json;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Wire-format tests for the "Trajectories and result files" region.
/// </summary>
public class WireFormatTrajectoryTests
{
    [Fact]
    public async Task ExistTrajectoryNamesAsync_PassesFileAndNames()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[true,false]");
        var el = await h.Dymola.ExistTrajectoryNamesAsync("r.mat", new[] { "a", "b" });
        Assert.Equal("existTrajectoryNames", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.Param(1).GetArrayLength());
        Assert.NotNull(el);
    }

    [Fact]
    public async Task GetLastResultFileNameAsync_ReturnsString()
    {
        using var h = new DymolaTestHarness();
        h.SetResultString("C:/out/dsres.mat");
        var s = await h.Dymola.GetLastResultFileNameAsync();
        Assert.Equal("C:/out/dsres.mat", s);
        Assert.Equal("Dymola_getLastResultFileName", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task InterpolateTrajectoryAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[[0,1]]");
        var el = await h.Dymola.InterpolateTrajectoryAsync("r.mat", new[] { "a" }, new[] { 0.0, 1.0 });
        Assert.Equal("interpolateTrajectory", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task ReadMatrixAsync_ReturnsRaw()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[[1,2],[3,4]]");
        var el = await h.Dymola.ReadMatrixAsync("m.mat", "A", 2, 2);
        Assert.Equal("readMatrix", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task ReadMatrixSizeAsync_PassesFileAndName()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[2,2]");
        var el = await h.Dymola.ReadMatrixSizeAsync("m.mat", "A");
        Assert.Equal("readMatrixSize", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task ReadStringMatrixAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[[\"a\"]]");
        var el = await h.Dymola.ReadStringMatrixAsync("m.mat", "S", 1);
        Assert.Equal("readStringMatrix", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task ReadTrajectoryAsync_PassesSignalsAndRows()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[[0,1]]");
        var el = await h.Dymola.ReadTrajectoryAsync("r.mat", new[] { "s" }, 10);
        Assert.Equal("readTrajectory", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task ReadTrajectoryNamesAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[\"time\",\"a\"]");
        var el = await h.Dymola.ReadTrajectoryNamesAsync("r.mat");
        Assert.Equal("readTrajectoryNames", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task ReadTrajectorySizeAsync_ReturnsInt()
    {
        using var h = new DymolaTestHarness();
        h.SetResultDouble(514.0);
        var n = await h.Dymola.ReadTrajectorySizeAsync("r.mat");
        Assert.Equal(514, n);
        Assert.Equal("readTrajectorySize", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task RemoveResultsAsync_PassesArray()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.RemoveResultsAsync(new[] { "a", "b", "c" });
        Assert.Equal("removeResults", h.Handler.LastRequest.Method);
        Assert.Equal(3, h.Handler.LastRequest.Param(0).GetArrayLength());
    }

    [Fact]
    public async Task WriteMatrixAsync_PassesNestedMatrix()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.WriteMatrixAsync("m.mat", "A", new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 } });
        Assert.Equal("writeMatrix", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.Param(2).GetArrayLength());
    }

    [Fact]
    public async Task WriteTrajectoryAsync_PassesNestedValues()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.WriteTrajectoryAsync("r.mat", new[] { "a", "b" },
            new[] { new[] { 0.0, 1.0 }, new[] { 2.0, 3.0 } });
        Assert.Equal("writeTrajectory", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SignalOperatorValueAsync_PassesOperatorAsInt()
    {
        using var h = new DymolaTestHarness();
        h.SetResultDouble(42.5);
        var v = await h.Dymola.SignalOperatorValueAsync("s", SignalOperator.RMS, 0.0, 10.0);
        Assert.Equal(42.5, v, precision: 4);
        Assert.Equal("signalOperatorValue", h.Handler.LastRequest.Method);
        // SignalOperator.RMS == 5
        Assert.Equal(5, h.Handler.LastRequest.Param(1).GetInt32());
    }

    [Fact]
    public async Task CalculateNumberPrecisionAsync_EmptyArray_UsesFillExpression()
    {
        using var h = new DymolaTestHarness();
        h.SetResultDouble(0.0);
        var n = await h.Dymola.CalculateNumberPrecisionAsync(Array.Empty<double>());
        Assert.Equal(0, n);
        Assert.Equal("calculateNumberPrecision", h.Handler.LastRequest.Method);
        // The fill(0, 0) expression is emitted as a JSON string param.
        Assert.Equal(JsonValueKind.String, h.Handler.LastRequest.Param(0).ValueKind);
        Assert.Contains("fill", h.Handler.LastRequest.Param(0).GetString());
    }

    [Fact]
    public async Task CalculateNumberPrecisionAsync_NonEmptyArray_SendsAsJsonArray()
    {
        using var h = new DymolaTestHarness();
        h.SetResultDouble(6.0);
        var n = await h.Dymola.CalculateNumberPrecisionAsync(new[] { 1.0, 2.0 });
        Assert.Equal(6, n);
        Assert.Equal(JsonValueKind.Array, h.Handler.LastRequest.Param(0).ValueKind);
        Assert.Equal(2, h.Handler.LastRequest.Param(0).GetArrayLength());
    }
}
