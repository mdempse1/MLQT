using System.Text.Json;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Wire-format tests for the "Variables, commands, errors" region of
/// <see cref="DymolaInterface"/>. Each test verifies that calling the C#
/// wrapper produces a JSON-RPC request with the Dymola-expected method name
/// and correctly-encoded parameters, and that the return value is extracted
/// from the canned response.
/// </summary>
public class WireFormatCoreTests
{
    [Fact]
    public async Task SetVariableAsync_DoubleValue_BuildsAssignmentCommand()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        var ok = await h.Dymola.SetVariableAsync("stopTime", 12.5);

        Assert.True(ok);
        // SetVariableAsync is implemented via ExecuteCommandAsync, which sends
        // the full assignment as the JSON-RPC method name.
        Assert.StartsWith("stopTime=", h.Handler.LastRequest.Method);
        Assert.Contains("12.5", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SetVariableAsync_BoolTrue_EncodesAsTrueLiteral()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SetVariableAsync("flag", true);
        Assert.Equal("flag=true", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SetVariableAsync_BoolFalse_EncodesAsFalseLiteral()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SetVariableAsync("flag", false);
        Assert.Equal("flag=false", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SetVariableAsync_String_WrapsInQuotes()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SetVariableAsync("fname", "abc");
        Assert.Equal("fname=\"abc\"", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SetVariableAsync_Expression_IsVerbatim()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SetVariableAsync("x", new ModelicaExpression("a+b"));
        Assert.Equal("x=a+b", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SetVariableAsync_StringArray_EncodesAsBraceList()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SetVariableAsync("names", new[] { "a", "b" });
        Assert.Equal("names={\"a\",\"b\"}", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task SetVariableAsync_DoubleArray_EncodesAsBraceList()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SetVariableAsync("vals", new double[] { 1.5, 2.5 });
        Assert.Equal("vals={1.5,2.5}", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExecuteCommandAsync_PassesCommandAsMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        var ok = await h.Dymola.ExecuteCommandAsync("foo()");
        Assert.True(ok);
        Assert.Equal("foo()", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task GetLastErrorAsync_ReturnsString()
    {
        using var h = new DymolaTestHarness();
        h.SetResultString("boom");
        var s = await h.Dymola.GetLastErrorAsync();
        Assert.Equal("boom", s);
        Assert.Equal("getLastError", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task GetLastErrorLogAsync_ExtractsStringFromArray()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("[\"an error\",1]");
        var s = await h.Dymola.GetLastErrorLogAsync();
        Assert.Equal("an error", s);
    }

    [Fact]
    public async Task GetLastErrorLogAsync_NonArrayResult_ReturnsEmpty()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("42");
        var s = await h.Dymola.GetLastErrorLogAsync();
        Assert.Equal(string.Empty, s);
    }

    [Fact]
    public async Task GetLastErrorRawAsync_ReturnsRawElement()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("{\"message\":\"x\"}");
        var el = await h.Dymola.GetLastErrorRawAsync();
        Assert.NotNull(el);
        Assert.Equal(JsonValueKind.Object, el!.Value.ValueKind);
    }

    [Fact]
    public async Task AddModelicaPathAsync_SendsPathAndEraseFlag()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        var ok = await h.Dymola.AddModelicaPathAsync("C:/Libs", erase: true);
        Assert.True(ok);
        Assert.Equal("AddModelicaPath", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.ParamCount);
        Assert.Equal("\"C:/Libs\"", h.Handler.LastRequest.Param(0).GetString());
        Assert.True(h.Handler.LastRequest.Param(1).GetBoolean());
    }

    [Fact]
    public async Task CdAsync_ReturnsStringResult()
    {
        using var h = new DymolaTestHarness();
        h.SetResultString("/tmp");
        var s = await h.Dymola.CdAsync("/tmp");
        Assert.Equal("/tmp", s);
        Assert.Equal("cd", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ClearAsync_DefaultsToSlowClear()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        Assert.True(await h.Dymola.ClearAsync());
        Assert.Equal("clear", h.Handler.LastRequest.Method);
        Assert.False(h.Handler.LastRequest.Param(0).GetBoolean());
    }

    [Fact]
    public async Task ClearAsync_FastFlag_IsPassedThrough()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        Assert.True(await h.Dymola.ClearAsync(fast: true));
        Assert.True(h.Handler.LastRequest.Param(0).GetBoolean());
    }

    [Fact]
    public async Task ClearFlagsAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        Assert.True(await h.Dymola.ClearFlagsAsync());
        Assert.Equal("clearFlags", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ClearLogAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        Assert.True(await h.Dymola.ClearLogAsync());
        Assert.Equal("clearlog", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task DefaultModelicaVersionAsync_PassesVersionAndFlag()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.DefaultModelicaVersionAsync("4.1.0", forceUpgrade: true);
        Assert.Equal("DefaultModelicaVersion", h.Handler.LastRequest.Method);
        Assert.Equal("\"4.1.0\"", h.Handler.LastRequest.Param(0).GetString());
        Assert.True(h.Handler.LastRequest.Param(1).GetBoolean());
    }

    [Fact]
    public async Task DocumentAsync_PassesFunctionName()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.DocumentAsync("simulateModel");
        Assert.Equal("document", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task DymolaLicenseInfoAsync_ReturnsRawElement()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("{\"user\":\"me\"}");
        var el = await h.Dymola.DymolaLicenseInfoAsync();
        Assert.Equal("DymolaLicenseInfo", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task DymolaVersionAsync_ReturnsString()
    {
        using var h = new DymolaTestHarness();
        h.SetResultString("Dymola 2026x");
        var s = await h.Dymola.DymolaVersionAsync();
        Assert.Equal("Dymola 2026x", s);
    }

    [Fact]
    public async Task DymolaVersionNumberAsync_ReturnsDouble()
    {
        using var h = new DymolaTestHarness();
        h.SetResultDouble(2026.1);
        var d = await h.Dymola.DymolaVersionNumberAsync();
        Assert.Equal(2026.1, d, precision: 4);
    }

    [Fact]
    public async Task EraseClassesAsync_SendsArrayParam()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.EraseClassesAsync(new[] { "A", "B" });
        Assert.Equal("eraseClasses", h.Handler.LastRequest.Method);
        Assert.Equal(1, h.Handler.LastRequest.ParamCount);
        Assert.Equal(JsonValueKind.Array, h.Handler.LastRequest.Param(0).ValueKind);
        Assert.Equal(2, h.Handler.LastRequest.Param(0).GetArrayLength());
    }

    [Fact]
    public async Task ExecuteAsync_PassesFilenameAndFlags()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExecuteAsync("s.mos", wait: false, terminal: true);
        Assert.Equal("Execute", h.Handler.LastRequest.Method);
        Assert.False(h.Handler.LastRequest.Param(1).GetBoolean());
        Assert.True(h.Handler.LastRequest.Param(2).GetBoolean());
    }

    [Fact]
    public async Task ExitAsync_UsesExitMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ExitAsync(2);
        Assert.Equal("exit", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.Param(0).GetInt32());
    }

    [Fact]
    public async Task GetDymolaCompilerAsync_ReturnsRawElement()
    {
        using var h = new DymolaTestHarness();
        h.SetResultJson("\"MSVC\"");
        var el = await h.Dymola.GetDymolaCompilerAsync();
        Assert.Equal("GetDymolaCompiler", h.Handler.LastRequest.Method);
        Assert.NotNull(el);
    }

    [Fact]
    public async Task GetRegistryValueAsync_ReturnsString()
    {
        using var h = new DymolaTestHarness();
        h.SetResultString("42");
        var s = await h.Dymola.GetRegistryValueAsync("HKCU", "foo", convert: true, defaultValue: "d");
        Assert.Equal("42", s);
        Assert.Equal("getRegistryValue", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task ListAsync_DefaultsToStarVariable()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ListAsync();
        Assert.Equal("list", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.ParamCount);
        Assert.Equal(JsonValueKind.Array, h.Handler.LastRequest.Param(1).ValueKind);
    }

    [Fact]
    public async Task ListFunctionsAsync_PassesFilter()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ListFunctionsAsync("plot*", longForm: true);
        Assert.Equal("listfunctions", h.Handler.LastRequest.Method);
        Assert.True(h.Handler.LastRequest.Param(1).GetBoolean());
    }

    [Fact]
    public async Task RequestOptionAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.RequestOptionAsync("Standard");
        Assert.Equal("RequestOption", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task RunScriptAsync_PassesScriptPath()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.RunScriptAsync("s.mos", silent: true, scriptDir: "/tmp");
        Assert.Equal("RunScript", h.Handler.LastRequest.Method);
        Assert.True(h.Handler.LastRequest.Param(1).GetBoolean());
    }

    [Fact]
    public async Task SetDymolaCompilerAsync_DefaultsSettingsArray()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SetDymolaCompilerAsync("MSVC");
        Assert.Equal("SetDymolaCompiler", h.Handler.LastRequest.Method);
        Assert.Equal(3, h.Handler.LastRequest.ParamCount);
        Assert.Equal(JsonValueKind.Array, h.Handler.LastRequest.Param(1).ValueKind);
    }

    [Fact]
    public async Task ShowComponentAsync_PassesComponentsArray()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ShowComponentAsync("model", new[] { "c1", "c2" });
        Assert.Equal("ShowComponent", h.Handler.LastRequest.Method);
        Assert.Equal(2, h.Handler.LastRequest.Param(1).GetArrayLength());
    }

    [Fact]
    public async Task ShowMessageWindowAsync_PassesFlag()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.ShowMessageWindowAsync(true);
        Assert.Equal("showMessageWindow", h.Handler.LastRequest.Method);
        Assert.True(h.Handler.LastRequest.Param(0).GetBoolean());
    }

    [Fact]
    public async Task SystemAsync_PassesCommandString()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.SystemAsync("dir");
        Assert.Equal("system", h.Handler.LastRequest.Method);
    }

    [Fact]
    public async Task TraceAsync_PassesAllFiveArgs()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.TraceAsync(variables: true, statements: true, calls: true,
            onlyFunction: "foo", profile: true);
        Assert.Equal("trace", h.Handler.LastRequest.Method);
        Assert.Equal(5, h.Handler.LastRequest.ParamCount);
    }

    [Fact]
    public async Task VerifyCompilerAsync_UsesCorrectMethod()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        await h.Dymola.VerifyCompilerAsync();
        Assert.Equal("verifyCompiler", h.Handler.LastRequest.Method);
    }
}
