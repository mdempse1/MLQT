using System.Text.Json;

namespace MLQT.Journeys;

/// <summary>
/// Compares a host's self-test report against the committed MAUI baseline.
/// </summary>
/// <remarks>
/// <para>This is the mechanism phase 7a exists to leave behind. When Photino replaces MAUI the
/// question "does the new host still work?" is answered by running the same route and diffing, rather
/// than by opening the application and forming an impression of it.</para>
///
/// <para>Written and exercised now, against the test host, rather than at the point of the migration.
/// A comparison written on the day it is first needed is a comparison nobody has ever seen fail.</para>
/// </remarks>
public static class HostConformance
{
    /// <summary>One probe that answered differently on the two hosts.</summary>
    public sealed record Difference(string Id, string Baseline, string Actual, string Detail)
    {
        public override string ToString() => $"{Id}: MAUI={Baseline}, this host={Actual} ({Detail})";
    }

    /// <summary>The committed MAUI report — the reference every later host is measured against.</summary>
    public static SelfTestJourney.Report MauiBaseline()
    {
        var path = Path.Combine(RepositoryRoot(), "MLQT.Shared.Tests", "TestFiles", "selftest-baseline-maui.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"the MAUI conformance baseline is missing from {path}", path);

        return JsonSerializer.Deserialize<SelfTestJourney.Report>(File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("the baseline did not parse");
    }

    /// <summary>
    /// Every probe whose status differs, plus every probe present on one side and not the other.
    /// </summary>
    /// <remarks>
    /// A missing probe is reported as a difference rather than skipped. The failure this guards
    /// against is not a host that answers differently — that is loud — but one where the sets have
    /// drifted apart and the intersection still matches, so the diff comes back empty and is read as
    /// success.
    /// </remarks>
    public static IReadOnlyList<Difference> Compare(
        SelfTestJourney.Report baseline,
        SelfTestJourney.Report actual)
    {
        var left = baseline.Probes.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var right = actual.Probes.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var differences = new List<Difference>();

        foreach (var id in left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var onLeft = left.TryGetValue(id, out var b);
            var onRight = right.TryGetValue(id, out var a);

            if (!onRight)
                differences.Add(new Difference(id, b!.Status, "(not run)", "the baseline has this probe and this host did not run it"));
            else if (!onLeft)
                differences.Add(new Difference(id, "(no baseline)", a!.Status, "this host ran a probe the baseline has no answer for"));
            else if (b!.Status != a!.Status)
                differences.Add(new Difference(id, b.Status, a.Status, a.Detail));
        }

        return differences;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }
}
