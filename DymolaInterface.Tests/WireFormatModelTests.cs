using System.Text.Json;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Wire-format tests for the "Models / files / FMU" region.
/// </summary>
public class WireFormatModelTests
{
    [Fact]
    public async Task CheckConversionAsync_SendsAllParams()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.CheckConversionAsync("lib", "2026", new[] { "2025", "2024" }, "report.txt", true);
        Assert.Equal("checkConversion", h.Handler.LastRequest.Method);
        Assert.Equal(5, h.Handler.LastRequest.ParamCount);
        Assert.Equal(2, h.Handler.LastRequest.Param(2).GetArrayLength());
    }

    [Fact]
    public async Task CheckModelAsync_DefaultFlags()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.CheckModelAsync("M");
        Assert.Equal("checkModel", h.Handler.LastRequest.Method);
        Assert.False(h.Handler.LastRequest.Param(1).GetBoolean());
        Assert.False(h.Handler.LastRequest.Param(2).GetBoolean());
    }

    [Fact]
    public async Task GetClassTextAsync_ReturnsString()
    {
        using var h = new DymolaTestHarness();
        h.SetResultString("model M end M;");
        var s = await h.Dymola.GetClassTextAsync("M", includeAnnotations: true, formatted: true);
        Assert.Equal("model M end M;", s);
        Assert.Equal("getClassText", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task GetDependentLibrariesAsync_ReturnsRaw()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[\"L1\",\"L2\"]");
        var el = await h.Dymola.GetDependentLibrariesAsync("M", dependentModels: true);
        Assert.Equal("getDependentLibraries", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task ImportFMUAsync_PassesEightArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ImportFMUAsync("f.fmu", true, true, false, "pkg", true, "M", false);
        Assert.Equal("importFMU", h.Handler.LastRequest.Method);
        Assert.Equal(8, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task ImportInitialAsync_DefaultsToDsFinalTxt()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ImportInitialAsync();
        Assert.Equal("importInitial", h.Handler.LastRequest.Method);
        Assert.Equal("\"dsfinal.txt\"", h.Handler.LastRequest.Param(0).GetString());
    }

    [Fact]
    public async Task ImportInitialResultAsync_PassesTime()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ImportInitialResultAsync("r.mat", 1.0);
        Assert.Equal("importInitialResult", h.Handler.LastRequest.Method);
        Assert.Equal(1.0, h.Handler.LastRequest.Param(1).GetDouble());
    }

    [Fact]
    public async Task ImportSSPAsync_DefaultArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ImportSSPAsync("file.ssp");
        Assert.Equal("importSSP", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ImportSSVAsync_DefaultArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ImportSSVAsync("file.ssv");
        Assert.Equal("importSSV", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExportSSPAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportSSPAsync("M", "out.ssp");
        Assert.Equal("exportSSP", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExportDiagramAsync_PassesAllSixArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportDiagramAsync("p.png", 100, 200, true, "M", true);
        Assert.Equal("exportDiagram", h.Handler.LastRequest.Method);
        Assert.Equal(6, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task ExportDocumentationAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportDocumentationAsync("out", "M");
        Assert.Equal("exportDocumentation", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExportEquationsAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportEquationsAsync("out.eq");
        Assert.Equal("exportEquations", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExportHTMLDirectoryAsync_DefaultsListsArrays()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportHTMLDirectoryAsync("MyLib");
        Assert.Equal("exportHTMLDirectory", h.Handler.LastRequest.Method);
        Assert.Equal(5, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task ExportIconAsync_DefaultDimensions()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportIconAsync("icon.png");
        Assert.Equal("exportIcon", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExportInitialAsync_AllArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportInitialAsync("ds", "script.mos", true, true);
        Assert.Equal("exportInitial", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExportInitialDsinAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExportInitialDsinAsync("s.mos");
        Assert.Equal("exportInitialDsin", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task OpenModelAsync_DefaultFlags()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.OpenModelAsync("lib/package.mo");
        Assert.Equal("openModel", h.Handler.LastRequest.Method);
        Assert.True(h.Handler.LastRequest.Param(1).GetBoolean());
        Assert.True(h.Handler.LastRequest.Param(2).GetBoolean());
    }

    [Fact]
    public async Task OpenModelFileAsync_AllDefaults()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.OpenModelFileAsync("Modelica.Blocks");
        Assert.Equal("openModelFile", h.Handler.LastRequest.Method);
        Assert.Equal(4, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task SaveEncryptedModelAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SaveEncryptedModelAsync("M", "out.moc");
        Assert.Equal("saveEncryptedModel", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SaveLogAsync_DefaultPath()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SaveLogAsync();
        Assert.Equal("savelog", h.Handler.LastRequest.Method);
        Assert.Equal("\"dymolalg.txt\"", h.Handler.LastRequest.Param(0).GetString());
    }

    [Fact]
    public async Task SaveModelAsync_DefaultVariant()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SaveModelAsync("M", "M.mo");
        Assert.Equal("saveModel", h.Handler.LastRequest.Method);
        Assert.Equal(0, h.Handler.LastRequest.Param(2).GetInt32());
    }

    [Fact]
    public async Task SaveSettingsAsync_AllNineArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SaveSettingsAsync("s.mos");
        Assert.Equal("saveSettings", h.Handler.LastRequest.Method);
        Assert.Equal(9, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task SaveTotalModelAsync_DefaultFlags()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SaveTotalModelAsync("out.mo", "M");
        Assert.Equal("saveTotalModel", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SetClassTextAsync_PassesParentAndText()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SetClassTextAsync("Parent", "class foo end foo;");
        Assert.Equal("setClassText", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task TranslateModelAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.TranslateModelAsync("M");
        Assert.Equal("translateModel", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task TranslateModelExportAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.TranslateModelExportAsync("M");
        Assert.Equal("translateModelExport", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task TranslateModelFMUAsync_EightArgsInOrder()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.TranslateModelFMUAsync("M", true, "MyFMU", "2.0", "all", false, true, true);
        Assert.Equal("translateModelFMU", h.Handler.LastRequest.Method);
        Assert.Equal(8, h.Handler.LastRequest.ParamCount);
        Assert.Equal("\"2.0\"", h.Handler.LastRequest.Param(3).GetString());
        Assert.Equal("\"all\"", h.Handler.LastRequest.Param(4).GetString());
    }
}
