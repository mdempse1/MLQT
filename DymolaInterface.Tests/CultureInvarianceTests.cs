using System.Globalization;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Regression tests guarding that numeric values emitted into Modelica command
/// strings always use '.' as the decimal separator, regardless of the host
/// machine's locale. Modelica (and Dymola's scripting server) always expect a
/// decimal point and never accept ',' as a separator. These tests force a
/// comma-decimal culture (de-DE) on the executing thread to prove the encoding
/// is invariant in the library itself — i.e. every consumer is protected, not
/// just hosts that happen to set an invariant default culture.
/// </summary>
public class CultureInvarianceTests
{
    /// <summary>Runs <paramref name="body"/> with the thread forced to a comma-decimal culture.</summary>
    private static async Task WithCommaDecimalCulture(Func<Task> body)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            // Sanity check the chosen culture actually formats with a comma, so the
            // test genuinely exercises the invariant-culture path.
            Assert.Equal("3,14", 3.14.ToString());
            await body();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task SetVariableAsync_DoubleScalar_UsesDecimalPoint_UnderCommaCulture()
    {
        await WithCommaDecimalCulture(async () =>
        {
            using var h = new DymolaTestHarness();
            h.SetResultBool(true);
            await h.Dymola.SetVariableAsync("stopTime", 12.5);
            Assert.Equal("stopTime=12.5", h.Handler.LastRequest.Method);
        });
    }

    [Fact]
    public async Task SetVariableAsync_DoubleArray_UsesDecimalPoint_UnderCommaCulture()
    {
        await WithCommaDecimalCulture(async () =>
        {
            using var h = new DymolaTestHarness();
            h.SetResultBool(true);
            await h.Dymola.SetVariableAsync("vals", new double[] { 1.5, 2.5 });
            Assert.Equal("vals={1.5,2.5}", h.Handler.LastRequest.Method);
        });
    }

    [Fact]
    public async Task PlotAsync_DoubleNamedArgument_UsesDecimalPoint_UnderCommaCulture()
    {
        await WithCommaDecimalCulture(async () =>
        {
            using var h = new DymolaTestHarness();
            h.SetResultBool(true);

            // thicknesses flows through FixNamedArgument -> FormatModelicaArray.
            await h.Dymola.PlotAsync(y: new[] { "a" }, thicknesses: new[] { 1.5 });

            var allParams = string.Concat(
                Enumerable.Range(0, h.Handler.LastRequest.ParamCount)
                    .Select(i => h.Handler.LastRequest.Param(i).GetString()));
            Assert.Contains("thicknesses={1.5}", allParams);
            Assert.DoesNotContain("1,5", allParams);
        });
    }
}
