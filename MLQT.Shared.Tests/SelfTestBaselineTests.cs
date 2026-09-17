using System.Text.Json;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The committed MAUI conformance baseline.
/// </summary>
/// <remarks>
/// <para>This file is the reason phase 7a had to happen before phase 7b. It records how the
/// application behaves under the host that is being replaced, captured while that host was still the
/// reference implementation — and it cannot be produced afterwards. Photino conformance is then a
/// diff against a file rather than somebody's judgement that things look about right.</para>
///
/// <para>These tests guard the artefact itself. A baseline that has quietly become unreadable, or
/// whose probes no longer correspond to the ones the route runs, is worse than none: the diff still
/// succeeds and says nothing.</para>
/// </remarks>
public class SelfTestBaselineTests
{
    public sealed record ProbeRow(string Id, string Name, string Status, string Detail);
    public sealed record Report(string Host, string RuntimeVersion, List<ProbeRow> Probes);

    /// <summary>The committed baseline, parsed.</summary>
    public static Report MauiBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "selftest-baseline-maui.json");
        Assert.True(File.Exists(path), $"the MAUI conformance baseline is missing from {path}");

        return JsonSerializer.Deserialize<Report>(File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("the baseline did not parse");
    }

    [Fact]
    public void ItWasCapturedUnderTheMauiHost()
    {
        // Not the test host, and not a test runner: the whole value of the file is that it says how
        // the application behaved under the host being replaced.
        Assert.Equal("MLQT", MauiBaseline().Host);
    }

    [Fact]
    public void EveryProbePassed()
    {
        // The baseline is a record of a host that worked. One captured with failures in it would
        // make those failures the standard Photino is held to.
        var failures = MauiBaseline().Probes.Where(p => p.Status != "Pass").ToList();

        Assert.True(failures.Count == 0,
            "the committed baseline records failing probes, which would make them the standard: "
            + string.Join("; ", failures.Select(f => $"{f.Id} = {f.Status} ({f.Detail})")));
    }

    [Fact]
    public void ItRecordsEveryProbeTheRouteRuns()
    {
        // The drift guard, and the one that matters most. A probe added to the route after the
        // baseline was captured has nothing to compare against; a probe removed leaves an entry that
        // can never be matched. Either way the diff still runs and still says "no differences",
        // which is the failure shape this repository has learned to distrust.
        //
        // Read from the route's own source rather than by running it, because running it needs a
        // host and this suite has none.
        var route = File.ReadAllText(Path.Combine(SharedDirectory(), "Pages", "SelfTest.razor.cs"));
        var declared = System.Text.RegularExpressions.Regex
            .Matches(route, @"await Probe\(""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(declared.Count > 10, $"only found {declared.Count} probes in the route source");

        var recorded = MauiBaseline().Probes.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(declared.Except(recorded));   // a probe with no baseline answer
        Assert.Empty(recorded.Except(declared));   // a baseline answer for a probe that no longer runs
    }

    [Fact]
    public void EveryProbeIdAppearsOnce()
    {
        var ids = MauiBaseline().Probes.Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void TheProbesTheMigrationIsMostLikelyToBreak_AreInIt()
    {
        // Named explicitly so that dropping one is a decision rather than an omission. These are the
        // ones the design note predicts will go wrong on a new host, and a baseline without them
        // would let the migration's likeliest failures through unnoticed.
        //
        // The last two were added before 7b started, and could not have been added afterwards: a new
        // probe has no MAUI answer once MAUI has stopped building, and the drift guard above then
        // refuses the baseline. The probe set freezes when the host is retired, not when 7a ended.
        var recorded = MauiBaseline().Probes.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var id in new[]
                 {
                     "assets.rcl", "scripts.globals", "cytoscape.init", "cytoscape.layouts",
                     "mudblazor.overlays", "settings.location",
                 })
            Assert.Contains(id, recorded);
    }

    private static string SharedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared");
            if (File.Exists(Path.Combine(candidate, "_Imports.razor")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("MLQT.Shared sources not found");
    }
}
