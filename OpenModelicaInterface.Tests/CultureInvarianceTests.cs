using System.Globalization;
using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Regression tests guarding that numeric values interpolated into OMC command
/// strings always use '.' as the decimal separator, regardless of the host
/// machine's locale. Modelica/OMC always expect a decimal point and never accept
/// ',' as a separator. These tests force a comma-decimal culture (de-DE) on the
/// executing thread to prove the command builder is invariant in the library
/// itself, protecting every consumer rather than only hosts that set an invariant
/// default culture. No running OMC process is required — they exercise the pure
/// <c>BuildSimulateCommand</c> string builder directly.
/// </summary>
public class CultureInvarianceTests
{
    // The type and its namespace share the name "OpenModelicaInterface", so the
    // type must be referenced with a global:: qualifier from this namespace.
    private static string Build(double startTime, double stopTime, int intervals, double tolerance) =>
        global::OpenModelicaInterface.OpenModelicaInterface.BuildSimulateCommand(
            "M", startTime, stopTime, intervals, tolerance, "dassl");

    [Fact]
    public void BuildSimulateCommand_UsesDecimalPoint_UnderCommaCulture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            // Sanity check the chosen culture really formats with a comma.
            Assert.Equal("3,14", 3.14.ToString());

            var command = Build(startTime: 0.5, stopTime: 4.0, intervals: 500, tolerance: 0.0001);

            // Exact wire format pins the whole command; the extra checks make the
            // culture intent explicit (decimal point used, comma-decimal not used).
            Assert.Equal(
                "simulate(M, startTime=0.5, stopTime=4, numberOfIntervals=500, tolerance=0.0001, method=\"dassl\")",
                command);
            Assert.Contains("startTime=0.5", command);
            Assert.Contains("tolerance=0.0001", command);
            Assert.DoesNotContain("0,5", command);
            Assert.DoesNotContain("0,0001", command);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
