using System.Text.Json;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Wire-format tests for the "Simulation / experiment / linearization" region.
/// </summary>
public class WireFormatSimulationTests
{
    [Fact]
    public async Task ExperimentAsync_DefaultParams()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExperimentAsync(stopTime: 10.0);
        Assert.Equal("experiment", h.Handler.LastRequest.Method);
        Assert.Equal(7, h.Handler.LastRequest.ParamCount);
        Assert.Equal(10.0, h.Handler.LastRequest.Param(1).GetDouble());
    }

    [Fact]
    public async Task ExperimentGetOutputAsync_ReturnsRaw()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("{\"a\":1}");
        var el = await h.Dymola.ExperimentGetOutputAsync();
        Assert.Equal("experimentGetOutput", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task ExperimentSetupOutputAsync_ElevenFlags()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExperimentSetupOutputAsync();
        Assert.Equal("experimentSetupOutput", h.Handler.LastRequest.Method);
        Assert.Equal(11, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task GetExperimentAsync_ReturnsRaw()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("{\"startTime\":0}");
        var el = await h.Dymola.GetExperimentAsync();
        Assert.Equal("getExperiment", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task InitializedAsync_DefaultFlags()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.InitializedAsync();
        Assert.Equal("initialized", h.Handler.LastRequest.Method);
        Assert.False(h.Handler.LastRequest.Param(0).GetBoolean());
        Assert.True(h.Handler.LastRequest.Param(1).GetBoolean());
    }

    [Fact]
    public async Task LinearizeModelAsync_DefaultsAndResultFile()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.LinearizeModelAsync("M");
        Assert.Equal("linearizeModel", h.Handler.LastRequest.Method);
        Assert.Equal(9, h.Handler.LastRequest.ParamCount);
        Assert.Equal("\"dslin\"", h.Handler.LastRequest.Param(8).GetString());
    }

    [Fact]
    public async Task SimulateModelAsync_Defaults()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SimulateModelAsync("M", stopTime: 2.0);
        Assert.Equal("simulateModel", h.Handler.LastRequest.Method);
        Assert.Equal(9, h.Handler.LastRequest.ParamCount);
        Assert.Equal(2.0, h.Handler.LastRequest.Param(2).GetDouble());
        Assert.Equal("\"Dassl\"", h.Handler.LastRequest.Param(5).GetString());
    }

    [Fact]
    public async Task SimulateExtendedModelAsync_PassesNameAndValueArrays()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("true");
        await h.Dymola.SimulateExtendedModelAsync("M", 0.0, 1.0, 0, 0.0, "Dassl", 1e-4, 0.0,
            "dsres", new[] { "a", "b" }, new[] { 1.0, 2.0 }, new[] { "a" }, true);
        Assert.Equal("simulateExtendedModel", h.Handler.LastRequest.Method);
        Assert.Equal(13, h.Handler.LastRequest.ParamCount);
        Assert.Equal(2, h.Handler.LastRequest.Param(9).GetArrayLength());
        Assert.Equal(2, h.Handler.LastRequest.Param(10).GetArrayLength());
    }

    [Fact]
    public async Task SimulateMultiExtendedModelAsync_SerialisesNestedValueMatrix()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("true");
        await h.Dymola.SimulateMultiExtendedModelAsync("M", 0.0, 1.0, 0, 0.0, "Dassl", 1e-4, 0.0,
            "dsres",
            new[] { "a", "b" },
            new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 } },
            new[] { "a" },
            new[] { "r1", "r2" }, true);
        Assert.Equal("simulateMultiExtendedModel", h.Handler.LastRequest.Method);
        // The 3rd argument block (index 10) is a 2x2 matrix of doubles.
        Assert.Equal(JsonValueKind.Array, h.Handler.LastRequest.Param(10).ValueKind);
        Assert.Equal(2, h.Handler.LastRequest.Param(10).GetArrayLength());
    }

    [Fact]
    public async Task SimulateMultiResultsModelAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("true");
        await h.Dymola.SimulateMultiResultsModelAsync("M", 0.0, 1.0, 0, 0.0, "Dassl", 1e-4, 0.0,
            "dsres",
            new[] { "p" },
            new[] { new[] { 1.0 } },
            new[] { "r" },
            new[] { "f1" }, true);
        Assert.Equal("simulateMultiResultsModel", h.Handler.LastRequest.Method);
    }
}
