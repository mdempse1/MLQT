using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The desktop hosts' self-test reports, captured and committed, compared with the MAUI baseline.
/// </summary>
/// <remarks>
/// <para>Phase 7b-5. <c>TheTestHostMatchesTheMauiBaseline</c> drives a live host over HTTP, which a
/// desktop host cannot be: Photino is a native window with no endpoint to attach to. Its report is
/// produced instead by running the published host with <c>MLQT_SELFTEST=1</c> and
/// <c>MLQT_SELFTEST_OUT</c> pointed at a file, and that file is committed.</para>
///
/// <para><b>This is a record rather than a live check, and the distinction matters.</b> It says what
/// Photino answered on the day, and it goes stale silently if nobody re-captures. What it does catch
/// is the failure that is actually likely: a probe added to <c>SelfTest</c>, or a baseline
/// re-captured on one host and not the other, so that the two sets drift apart. Before 7b-5 the only
/// evidence Photino had ever matched was a terminal window that had since been closed.</para>
///
/// <para>No fixture: these read two files. They cost nothing and run wherever this suite does.</para>
///
/// <para>To re-capture, from a published Photino host:
/// <code>
/// set MLQT_SELFTEST=1 &amp; set MLQT_SELFTEST_HOST=MLQT.Photino
/// set MLQT_SELFTEST_OUT=&lt;repo&gt;\MLQT.Shared.Tests\TestFiles\selftest-photino-windows.json
/// MLQT.Photino.exe
/// </code>
/// The host writes the report and exits.</para>
/// </remarks>
public class DesktopHostConformanceTests
{
    private const string PhotinoWindows = "selftest-photino-windows.json";

    [Fact]
    public void PhotinoOnWindowsMatchesTheMauiBaseline()
    {
        // The whole point of phase 7a, collected: the new host answers every probe the way the old
        // one did. Captured 2026-09-08 against the published Windows build.
        var actual = HostConformance.Captured(PhotinoWindows);

        var differences = HostConformance.Compare(HostConformance.MauiBaseline(), actual);

        Assert.True(differences.Count == 0,
            "Photino on Windows answers differently from the MAUI baseline:"
            + string.Concat(differences.Select(d => Environment.NewLine + "  " + d)));
    }

    [Fact]
    public void TheCaptureIsFromPhotinoAndNotACopyOfTheBaseline()
    {
        // Compare ignores the Host field by design - two hosts is the whole point - so a capture that
        // was accidentally overwritten with the baseline would compare clean and prove nothing. The
        // one field that says which host produced it is therefore the one to assert on.
        var actual = HostConformance.Captured(PhotinoWindows);

        Assert.Equal("MLQT.Photino", actual.Host);
        Assert.NotEqual(HostConformance.MauiBaseline().Host, actual.Host);
    }

    [Fact]
    public void TheCaptureRanEveryProbe()
    {
        // A host that stops early leaves a short report, and a short report compared against the
        // baseline is caught by Compare - but only while the baseline is longer. Stating the count
        // holds it even if both were re-captured from a truncated run.
        var actual = HostConformance.Captured(PhotinoWindows);

        Assert.Equal(HostConformance.MauiBaseline().Probes.Count, actual.Probes.Count);
        Assert.All(actual.Probes, p => Assert.Equal("Pass", p.Status));
    }
}
