using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The test host measured against the committed MAUI baseline.
/// </summary>
/// <remarks>
/// <para>The comparison phase 7b will run against Photino, exercised now against a host that already
/// exists. Its value is not that the test host matches MAUI — it is a Blazor Server host and in
/// places it deliberately does not — but that the machinery producing the answer has been watched
/// working before the migration depends on it.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class HostConformanceJourney(TestHostFixture host)
{
    [Fact]
    public void TheBaselineIsReadable()
    {
        var baseline = HostConformance.MauiBaseline();

        Assert.Equal("MLQT", baseline.Host);
        Assert.NotEmpty(baseline.Probes);
    }

    [Fact]
    public void ComparingTheBaselineWithItself_FindsNothing()
    {
        var baseline = HostConformance.MauiBaseline();

        Assert.Empty(HostConformance.Compare(baseline, baseline));
    }

    [Fact]
    public void ADroppedProbeIsADifference()
    {
        // The case the comparison exists for. A host that stopped running a probe would otherwise
        // match on every probe it did run, and read as conformant.
        var baseline = HostConformance.MauiBaseline();
        var short_ = baseline with { Probes = baseline.Probes.Skip(1).ToList() };

        var difference = Assert.Single(HostConformance.Compare(baseline, short_));

        Assert.Equal(baseline.Probes[0].Id, difference.Id);
        Assert.Equal("(not run)", difference.Actual);
    }

    [Fact]
    public void AnUnknownProbeIsADifference()
    {
        var baseline = HostConformance.MauiBaseline();
        var extra = baseline with
        {
            Probes = [.. baseline.Probes, new SelfTestJourney.ProbeRow("new.probe", "n", "Pass", "d")],
        };

        var difference = Assert.Single(HostConformance.Compare(baseline, extra));

        Assert.Equal("new.probe", difference.Id);
        Assert.Equal("(no baseline)", difference.Baseline);
    }

    [Fact]
    public async Task TheTestHostMatchesTheMauiBaseline()
    {
        var actual = await SelfTestJourney.RunAsync(host);

        // Proof that this compared a live host against the file, and not the file against itself.
        // The comparison ignores the Host field by design - two hosts is the whole point - so nothing
        // else here would notice if RunAsync quietly gave back the baseline.
        Assert.Equal("MLQT.TestHost", actual.Host);

        var differences = HostConformance.Compare(HostConformance.MauiBaseline(), actual);

        Assert.True(differences.Count == 0,
            "the test host answers differently from the MAUI baseline:"
            + string.Concat(differences.Select(d => Environment.NewLine + "  " + d)));
    }
}
