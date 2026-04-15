using System.Text.Json;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Wire-format tests for the plotting region. These exercise the named-argument
/// serialisation used by PlotAsync / PrintPlotAsync as well as the heavier
/// one-liner wrappers (plotArray, plotArrays, plotDiscretized, plotExpression,
/// plotHeading, plotParametricCurve, plotScatter, plotSignalOperator,
/// plotTitle, createPlot, createTable, removePlots, ExportPlotAsImage, ...).
/// </summary>
public class WireFormatPlotTests
{
    [Fact]
    public async Task PlotAsync_EmptyArray_ReturnsFalseWithoutCallingServer()
    {
        using var h = new DymolaTestHarness();
        var ok = await h.Dymola.PlotAsync(Array.Empty<string>());
        Assert.False(ok);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task PlotAsync_BuildsYNamedArgument()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        var ok = await h.Dymola.PlotAsync(new[] { "PI.y", "PI.u_s" });
        Assert.True(ok);
        Assert.Equal("plot", h.Handler.LastRequest.Method);
        Assert.Equal(1, h.Handler.LastRequest.ParamCount);
        var p0 = h.Handler.LastRequest.Param(0);
        Assert.Equal(JsonValueKind.String, p0.ValueKind);
        var s = p0.GetString();
        Assert.StartsWith("y={", s);
        Assert.Contains("\"PI.y\"", s!);
        Assert.Contains("\"PI.u_s\"", s!);
    }

    [Fact]
    public async Task PlotAsync_WithAllOptions_EmitsNamedArguments()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        await h.Dymola.PlotAsync(
            y: new[] { "a" },
            legends: new[] { "leg" },
            plotInAll: true,
            colors: new[] { new[] { 255, 0, 0 } },
            patterns: new[] { LinePattern.Solid },
            markers: new[] { MarkerStyle.Circle },
            thicknesses: new[] { 1.5 },
            axes: new[] { 1 });

        var allParams = string.Concat(
            Enumerable.Range(0, h.Handler.LastRequest.ParamCount)
                .Select(i => h.Handler.LastRequest.Param(i).GetString()));
        Assert.Contains("legends={", allParams);
        Assert.Contains("plotInAll=", allParams);
        Assert.Contains("colors={", allParams);
        Assert.Contains("patterns={", allParams);
        Assert.Contains("markers={", allParams);
        Assert.Contains("thicknesses={", allParams);
        Assert.Contains("axes={", allParams);
    }

    [Fact]
    public async Task PlotArrayAsync_Defaults()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotArrayAsync(new[] { 0.0, 1.0 }, new[] { 1.0, 2.0 });
        Assert.Equal("plotArray", h.Handler.LastRequest.Method);
        Assert.Equal(12, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task PlotArraysAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotArraysAsync(
            new[] { 0.0, 1.0 },
            new[] { new[] { 1.0, 2.0 } },
            0, new[] { "a" }, 0, "t",
            new[] { new[] { 1, 2, 3 } },
            new[] { LinePattern.Solid },
            new[] { MarkerStyle.Circle },
            new[] { 1.0 }, new[] { 1 }, new[] { "s" });
        Assert.Equal("plotArrays", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotDiscretizedAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotDiscretizedAsync(new[] { "v" }, "leg", "time", 0, false);
        Assert.Equal("plotDiscretized", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotDocumentationAsync_DefaultArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotDocumentationAsync();
        Assert.Equal("plotDocumentation", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotExpressionAsync_EmitsExpressionVerbatim()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotExpressionAsync("sin(time)");
        Assert.Equal("plotExpression", h.Handler.LastRequest.Method);
        // ModelicaExpression is passed verbatim (string kind, no added quotes).
        Assert.Equal(JsonValueKind.String, h.Handler.LastRequest.Param(0).ValueKind);
        Assert.Equal("sin(time)", h.Handler.LastRequest.Param(0).GetString());
    }

    [Fact]
    public async Task PlotExternalAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotExternalAsync("file.mat");
        Assert.Equal("plotExternal", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotHeadingAsync_SendsStyleAsIntArray()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotHeadingAsync("Title", 14, "Arial", new[] { 0, 0, 0 },
            new[] { TextStyle.Bold }, TextAlignment.Center, 0);
        Assert.Equal("plotHeading", h.Handler.LastRequest.Method);
        // textStyle is param index 4 (textString, fontSize, fontName, lineColor,
        // textStyle, horizontalAlignment, id). TextStyle.Bold -> 1 so we expect
        // a one-element array; alignment Center -> 2.
        Assert.Equal(1, h.Handler.LastRequest.Param(4).GetArrayLength());
        Assert.Equal(2, h.Handler.LastRequest.Param(5).GetInt32());
    }

    [Fact]
    public async Task PlotMovingAverageAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotMovingAverageAsync("s", 0, 10, 0.1);
        Assert.Equal("plotMovingAverage", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotParametricCurveAsync_Defaults()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotParametricCurveAsync(new[] { 0.0, 1.0 }, new[] { 1.0, 2.0 }, new[] { 0.0, 1.0 });
        Assert.Equal("plotParametricCurve", h.Handler.LastRequest.Method);
        Assert.Equal(15, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task PlotParametricCurvesAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotParametricCurvesAsync(
            new[] { new[] { 0.0, 1.0 } },
            new[] { new[] { 1.0, 2.0 } },
            new[] { new[] { 0.0, 1.0 } },
            "x", "y", "s",
            new[] { "leg" },
            0,
            new[] { new[] { 0, 0, 0 } },
            new[] { LinePattern.Solid },
            new[] { MarkerStyle.Circle },
            new[] { 1.0 },
            false,
            new[] { 1 });
        Assert.Equal("plotParametricCurves", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotRowColumnLabelsAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotRowColumnLabelsAsync(new[] { "x" }, new[] { "y" }, 0);
        Assert.Equal("plotRowColumnLabels", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotScatterAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotScatterAsync(
            new[] { 0.0, 1.0 },
            new[] { 1.0, 2.0 },
            new[] { new[] { 255, 0, 0 } },
            new[] { MarkerStyle.Circle },
            "leg", 1, "unit", true, 0);
        Assert.Equal("plotScatter", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotSignalDifferenceAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotSignalDifferenceAsync("s");
        Assert.Equal("plotSignalDifference", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotSignalOperatorAsync_PassesOperatorAsInt()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotSignalOperatorAsync("s", SignalOperator.Max, 0.0, 10.0);
        Assert.Equal("plotSignalOperator", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.Param(1).GetInt32());
    }

    [Fact]
    public async Task PlotSignalOperatorHarmonicAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotSignalOperatorHarmonicAsync("s", SignalOperator.THD, 0.0, 1.0, 0.01, 0.02, 1, 1);
        Assert.Equal("plotSignalOperatorHarmonic", h.Handler.LastRequest.Method);
        // THD is enum value 8
        Assert.Equal(8, h.Handler.LastRequest.Param(1).GetInt32());
    }

    [Fact]
    public async Task PlotTextAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotTextAsync(new[] { 0.0, 0.0, 1.0, 1.0 }, "hi", 10, "Arial",
            new[] { 0, 0, 0 }, new[] { TextStyle.Italic }, TextAlignment.Left, 0);
        Assert.Equal("plotText", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PlotTitleAsync_DefaultArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotTitleAsync();
        Assert.Equal("plotTitle", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task PlotWindowSetupAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PlotWindowSetupAsync(3);
        Assert.Equal("plotWindowSetup", h.Handler.LastRequest.Method);
        Assert.Equal(3, h.Handler.LastRequest.Param(0).GetInt32());
    }

    [Fact]
    public async Task PrintPlotAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PrintPlotAsync(new[] { "a" });
        Assert.Equal("printPlot", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PrintPlotAsync_WithAllOptions_EmitsNamedArguments()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PrintPlotAsync(
            y: new[] { "a" },
            legends: new[] { "leg" },
            plotInAll: false,
            colors: new[] { new[] { 0, 255, 0 } },
            patterns: new[] { LinePattern.Dash },
            markers: new[] { MarkerStyle.Square },
            thicknesses: new[] { 2.0 },
            axes: new[] { 2 });
        Assert.Equal(8, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task PrintPlotArrayAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PrintPlotArrayAsync(new[] { 0.0 }, new[] { 1.0 });
        Assert.Equal("printPlotArray", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task PrintPlotArraysAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.PrintPlotArraysAsync(
            new[] { 0.0 }, new[] { new[] { 1.0 } }, 0, new[] { "l" }, 0, "t",
            new[] { new[] { 0, 0, 0 } }, new[] { LinePattern.Solid },
            new[] { MarkerStyle.None }, new[] { 1.0 }, new[] { 1 }, new[] { "u" });
        Assert.Equal("printPlotArrays", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ClearPlotAsync_DefaultId()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ClearPlotAsync();
        Assert.Equal("clearPlot", h.Handler.LastRequest.Method);
        Assert.Equal(0, h.Handler.LastRequest.Param(0).GetInt32());
    }

    [Fact]
    public async Task CreatePlotAsync_MegaArgList_IsSerialised()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.CreatePlotAsync(
            id: 0,
            position: new[] { 0, 0, 400, 300 },
            x: new[] { "time" },
            y: new[] { "a" },
            heading: "H",
            range: new[] { 0.0, 10.0 },
            erase: true, autoscale: true, autoerase: true, autoreplot: true,
            description: true, grid: true, color: true, online: false,
            legend: true, timeWindow: 0.0, filename: "",
            legendLocation: 0, legendHorizontal: true, legendFrame: true, supressMarker: false,
            logX: false, logY: false, legends: new[] { "leg" }, subPlot: new[] { 1 },
            uniformScaling: false, leftTitleType: 0, leftTitle: "L",
            bottomTitleType: 0, bottomTitle: "B",
            colors: new[] { new[] { 0, 0, 0 } },
            patterns: new[] { LinePattern.Solid },
            markers: new[] { MarkerStyle.None },
            thicknesses: new[] { 1.0 },
            range2: new[] { 0.0, 1.0 }, logY2: false, rightTitleType: 0, rightTitle: "R",
            axes: new[] { 1 }, timeUnit: "s",
            displayUnits: new[] { "u" }, showOriginal: true, showDifference: false,
            plotInAll: false);
        Assert.Equal("createPlot", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task CreateTableAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.CreateTableAsync(0, new[] { 0, 0, 400, 300 },
            new[] { "time" }, new[] { "a" }, "H", true, true, "",
            new[] { "l" }, "s", new[] { "u" }, true, false);
        Assert.Equal("createTable", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task RemovePlotsAsync_DefaultCloseResults()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.RemovePlotsAsync();
        Assert.Equal("removePlots", h.Handler.LastRequest.Method);
        Assert.True(h.Handler.LastRequest.Param(0).GetBoolean());
    }

    [Fact]
    public async Task ExportPlotAsImageAsync_DefaultArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportPlotAsImageAsync("out.png");
        Assert.Equal("ExportPlotAsImage", h.Handler.LastRequest.Method);
        Assert.Equal(4, h.Handler.LastRequest.ParamCount);
    }
}
