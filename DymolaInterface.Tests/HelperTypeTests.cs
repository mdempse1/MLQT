using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Unit tests for the simple helper types and static utilities in
/// <see cref="DymolaInterface"/>, plus the parameter-transformation behaviour
/// that is observable from outside via the wire format.
/// </summary>
public class HelperTypeTests
{
    [Fact]
    public void ModelicaExpression_StoresValue()
    {
        var e = new ModelicaExpression("fill(0, 0)");
        Assert.Equal("fill(0, 0)", e.Value);
    }

    [Fact]
    public void NamedArgument_StoresNameAndValue()
    {
        var na = new NamedArgument("y", new[] { "a", "b" });
        Assert.Equal("y", na.Name);
        Assert.IsType<string[]>(na.Value);
    }

    [Fact]
    public void NamedArgument_NullValue_IsAllowed()
    {
        var na = new NamedArgument("opt", null);
        Assert.Null(na.Value);
    }

    [Fact]
    public void MakeModelicaString_UnquotedInput_IsQuoted()
    {
        Assert.Equal("\"hello\"", DymolaInterface.MakeModelicaString("hello"));
    }

    [Fact]
    public void MakeModelicaString_PreQuotedInput_IsReturnedAsIs()
    {
        Assert.Equal("\"already quoted\"", DymolaInterface.MakeModelicaString("\"already quoted\""));
    }

    [Fact]
    public void CheckDymolaPathString_BackslashesConvertedToForwardSlashes_AndQuoted()
    {
        var p = DymolaInterface.CheckDymolaPathString(@"C:\Temp\foo");
        Assert.Equal("\"C:/Temp/foo\"", p);
    }

    [Fact]
    public void CheckDymolaPathString_ForwardSlashInput_IsQuoted()
    {
        var p = DymolaInterface.CheckDymolaPathString("C:/Temp/foo");
        Assert.Equal("\"C:/Temp/foo\"", p);
    }

    [Fact]
    public void CheckDymolaPathString_AlreadyQuoted_IsNotDoubleQuoted()
    {
        var p = DymolaInterface.CheckDymolaPathString("\"C:/Temp/foo\"");
        Assert.Equal("\"C:/Temp/foo\"", p);
    }

    [Theory]
    [InlineData(LinePattern.Default, 1)]
    [InlineData(LinePattern.None, 2)]
    [InlineData(LinePattern.Solid, 3)]
    [InlineData(LinePattern.Dash, 4)]
    [InlineData(LinePattern.Dot, 5)]
    [InlineData(LinePattern.DashDot, 6)]
    [InlineData(LinePattern.DashDotDot, 7)]
    public void LinePattern_EnumValues(LinePattern pattern, int expected)
        => Assert.Equal(expected, (int)pattern);

    [Theory]
    [InlineData(MarkerStyle.Default, 1)]
    [InlineData(MarkerStyle.Circle, 4)]
    [InlineData(MarkerStyle.AreaFill, 15)]
    public void MarkerStyle_EnumValues(MarkerStyle style, int expected)
        => Assert.Equal(expected, (int)style);

    [Theory]
    [InlineData(SignalOperator.Min, 1)]
    [InlineData(SignalOperator.Max, 2)]
    [InlineData(SignalOperator.ArithmeticMean, 3)]
    [InlineData(SignalOperator.RectifiedMean, 4)]
    [InlineData(SignalOperator.RMS, 5)]
    [InlineData(SignalOperator.ACCoupledRMS, 6)]
    [InlineData(SignalOperator.SlewRate, 7)]
    [InlineData(SignalOperator.THD, 8)]
    [InlineData(SignalOperator.FirstHarmonic, 9)]
    public void SignalOperator_EnumValues(SignalOperator op, int expected)
        => Assert.Equal(expected, (int)op);

    [Theory]
    [InlineData(TextAlignment.Left, 1)]
    [InlineData(TextAlignment.Center, 2)]
    [InlineData(TextAlignment.Right, 3)]
    public void TextAlignment_EnumValues(TextAlignment a, int expected)
        => Assert.Equal(expected, (int)a);

    [Theory]
    [InlineData(TextStyle.Bold, 1)]
    [InlineData(TextStyle.Italic, 2)]
    [InlineData(TextStyle.UnderLine, 3)]
    public void TextStyle_EnumValues(TextStyle s, int expected)
        => Assert.Equal(expected, (int)s);

    [Fact]
    public void Offline_SetOfflineMode_MutatesFlag()
    {
        using var di = new DymolaInterface("", 1, "127.0.0.1");
        // Constructor's ping fails because nothing is listening on port 1.
        Assert.True(di.IsOfflineMode());
        di.SetOfflineMode(false);
        Assert.False(di.IsOfflineMode());
        di.SetOfflineMode(true);
        Assert.True(di.IsOfflineMode());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var di = new DymolaInterface("", 1, "127.0.0.1");
        di.Dispose();
        // Second dispose should not throw.
        di.Dispose();
    }

    [Fact]
    public async Task StartDymolaProcessAsync_EmptyPath_ThrowsInvalidOperation()
    {
        using var di = new DymolaInterface("", 1, "127.0.0.1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => di.StartDymolaProcessAsync());
    }

    [Fact]
    public async Task StopDymolaProcessAsync_WhenNoProcess_DoesNotThrow()
    {
        using var di = new DymolaInterface("", 1, "127.0.0.1");
        await di.StopDymolaProcessAsync();
        Assert.True(di.IsOfflineMode());
    }

    [Fact]
    public async Task CallWhenOffline_ReturnsFalseNoRequest()
    {
        // A fresh DymolaInterface against an unreachable port is offline, so
        // any wrapper method short-circuits and returns the default.
        using var di = new DymolaInterface("", 1, "127.0.0.1");
        Assert.True(di.IsOfflineMode());

        var ok = await di.ExecuteCommandAsync("whatever");
        Assert.False(ok);

        var s = await di.GetLastErrorAsync();
        Assert.Equal(string.Empty, s);

        var d = await di.DymolaVersionNumberAsync();
        Assert.Equal(0.0, d);
    }

    [Fact]
    public async Task FixJsonParameter_StringIsQuoted_InWireFormat()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        await h.Dymola.CdAsync(@"C:\Temp");

        Assert.Equal("cd", h.Handler.LastRequest.Method);
        // A C# string becomes a Modelica string literal, i.e. the decoded
        // JSON value includes surrounding quotation marks and escaped
        // backslashes: "C:\\Temp".
        Assert.Equal("\"C:\\\\Temp\"", h.Handler.LastRequest.Param(0).GetString());
    }

    [Fact]
    public async Task FixJsonParameter_BackslashInString_IsEscaped()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        await h.Dymola.CdAsync("a\\b");

        // Each backslash in the C# input doubles for Modelica source.
        Assert.Equal("\"a\\\\b\"", h.Handler.LastRequest.Param(0).GetString());
    }

    [Fact]
    public async Task FixJsonParameter_QuoteInString_IsEscaped()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);

        await h.Dymola.CdAsync("a\"b");

        // Double-quote in the input becomes \" inside the Modelica literal.
        Assert.Equal("\"a\\\"b\"", h.Handler.LastRequest.Param(0).GetString());
    }

    [Fact]
    public async Task CallDymolaFunction_WhenHandlerReturnsError_ReturnsFalse()
    {
        using var h = new DymolaTestHarness();
        h.SetResultError("boom");

        var ok = await h.Dymola.ClearAsync();

        Assert.False(ok);
    }
}
