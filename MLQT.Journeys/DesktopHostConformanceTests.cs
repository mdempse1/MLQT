using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The desktop hosts' self-test reports, captured and committed, compared with the MAUI baseline.
/// </summary>
/// <remarks>
/// <para>Phase 7b-5 (Windows) and 7b-6 (Linux). <c>TheTestHostMatchesTheMauiBaseline</c> drives a
/// live host over HTTP, which a desktop host cannot be: Photino is a native window with no endpoint
/// to attach to. Its report is produced instead by running the published host with
/// <c>MLQT_SELFTEST=1</c> and <c>MLQT_SELFTEST_OUT</c> pointed at a file, and that file is
/// committed.</para>
///
/// <para><b>These are records rather than live checks, and the distinction matters.</b> They say what
/// Photino answered on the day, and they go stale silently if nobody re-captures. What they do catch
/// is the failure that is actually likely: a probe added to <c>SelfTest</c>, or a baseline
/// re-captured on one host and not the others, so that the sets drift apart. Before 7b-5 the only
/// evidence Photino had ever matched was a terminal window that had since been closed.</para>
///
/// <para><b>Both captures are asserted from every platform</b>, because they are files. A Linux
/// runner checks the Windows capture and vice versa, so neither can be quietly re-captured alone —
/// which is the drift this exists to catch, and it would not be caught by a test that only ran where
/// its own host does.</para>
///
/// <para><b>The baseline itself is frozen.</b> The MAUI host was deleted at the cutover (7b-8), so
/// <c>selftest-baseline-maui.json</c> cannot be re-captured — and it should not be. A baseline that
/// could be re-taken from the current host would only ever confirm that the host agrees with itself.
/// The Photino captures below are re-capturable, and the <c>desktop-selftest</c> CI job re-captures
/// them on a real runner on every push, which is what turns these records into a live check.</para>
///
/// <para>No fixture: these read two files. They cost nothing and run wherever this suite does.</para>
///
/// <para>To re-capture, from a published Photino host — Windows:
/// <code>
/// set MLQT_SELFTEST=1 &amp; set MLQT_SELFTEST_HOST=MLQT.Photino
/// set MLQT_SELFTEST_OUT=&lt;repo&gt;\MLQT.Shared.Tests\TestFiles\selftest-photino-windows.json
/// MLQT.Photino.exe
/// </code>
/// and Linux:
/// <code>
/// MLQT_SELFTEST=1 MLQT_SELFTEST_HOST=MLQT.Photino \
///   MLQT_SELFTEST_OUT=&lt;repo&gt;/MLQT.Shared.Tests/TestFiles/selftest-photino-linux.json \
///   ./MLQT.Photino
/// </code>
/// The host writes the report and exits.</para>
/// </remarks>
public class DesktopHostConformanceTests
{
    /// <summary>The committed captures, by the platform each was taken on.</summary>
    /// <remarks>
    /// One list rather than a pair of near-identical test methods, so a third host — macOS is named
    /// in the roadmap's sequence — is a line here and inherits all three assertions.
    /// </remarks>
    public static TheoryData<string, string> Captures() => new()
    {
        { "Windows", "selftest-photino-windows.json" },
        { "Linux", "selftest-photino-linux.json" },
    };

    [Theory]
    [MemberData(nameof(Captures))]
    public void PhotinoMatchesTheMauiBaseline(string platform, string capture)
    {
        // The whole point of phase 7a, collected: the new host answers every probe the way the old
        // one did. Captured 2026-09-08 (Windows) and 2026-09-09 (Linux) against published builds.
        var actual = HostConformance.Captured(capture);

        var differences = HostConformance.Compare(HostConformance.MauiBaseline(), actual);

        Assert.True(differences.Count == 0,
            $"Photino on {platform} answers differently from the MAUI baseline:"
            + string.Concat(differences.Select(d => Environment.NewLine + "  " + d)));
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void TheCaptureIsFromPhotinoAndNotACopyOfTheBaseline(string _, string capture)
    {
        // Compare ignores the Host field by design - two hosts is the whole point - so a capture that
        // was accidentally overwritten with the baseline would compare clean and prove nothing. The
        // one field that says which host produced it is therefore the one to assert on.
        var actual = HostConformance.Captured(capture);

        Assert.Equal("MLQT.Photino", actual.Host);
        Assert.NotEqual(HostConformance.MauiBaseline().Host, actual.Host);
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void TheCaptureRanEveryProbe(string _, string capture)
    {
        // A host that stops early leaves a short report, and a short report compared against the
        // baseline is caught by Compare - but only while the baseline is longer. Stating the count
        // holds it even if both were re-captured from a truncated run.
        var actual = HostConformance.Captured(capture);

        Assert.Equal(HostConformance.MauiBaseline().Probes.Count, actual.Probes.Count);
        Assert.All(actual.Probes, p => Assert.Equal("Pass", p.Status));
    }

    [Fact]
    public void TheTwoPlatformCapturesAreDistinct()
    {
        // The one failure the per-capture tests cannot see: a capture copied from the other platform
        // rather than taken, which passes all three above and proves nothing about the platform it
        // is named for. The paths a host reports are what differ - and they are what the file is
        // for, since Compare ignores Detail.
        var windows = HostConformance.Captured("selftest-photino-windows.json");
        var linux = HostConformance.Captured("selftest-photino-linux.json");

        Assert.StartsWith("C:\\", Detail(windows, "logging.writes"), StringComparison.Ordinal);
        Assert.StartsWith("/", Detail(linux, "logging.writes"), StringComparison.Ordinal);
    }

    private static string Detail(SelfTestJourney.Report report, string id) =>
        report.Probes.Single(p => p.Id == id).Detail;
}
