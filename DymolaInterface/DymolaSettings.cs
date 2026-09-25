namespace DymolaInterface;

public class DymolaSettings
{
    public string DymolaPath { get; set; } = string.Empty;
    public string HostAddress { get; } = "127.0.0.1";
    public int PortNumber { get; set; } = 8082;

    /// <summary>
    /// How long one command may take before MLQT stops waiting for it, in milliseconds; 0 for no
    /// limit. Five minutes by default, which is what the interface used before it could be changed.
    /// </summary>
    /// <remarks>
    /// <para>What a user raises for a model that takes longer to check than that (B263). Milliseconds,
    /// and named like <c>OpenModelicaSettings.CommandTimeoutMs</c>, so the two tools read the same way
    /// in the settings file and the dialog can treat them alike.</para>
    ///
    /// <para>An <c>int</c> of milliseconds stops at about 24.8 days, inside
    /// <see cref="DymolaInterface.MaxCommandTimeout"/> (≈49.7 days) — so no value this can hold is one
    /// the interface would refuse. That matters because a refused timeout throws, and the command
    /// wrapper that would catch it reads as every command failing.</para>
    /// </remarks>
    public int CommandTimeoutMs { get; set; } = 300_000;

    /// <summary>
    /// <see cref="CommandTimeoutMs"/> as the interface takes it: <see cref="Timeout.InfiniteTimeSpan"/>
    /// for 0 (no limit), and the five-minute default for a negative value, which the dialog does not
    /// allow and a hand-edited settings file might.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan CommandTimeout => CommandTimeoutMs switch
    {
        0 => Timeout.InfiniteTimeSpan,
        < 0 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMilliseconds(CommandTimeoutMs),
    };

    /// <summary>The oldest release auto-detection looks for, as MLQT's documented minimum.</summary>
    public const int OldestDetectedYear = 2021;

    public DymolaSettings()
    {
        if (string.IsNullOrEmpty(DymolaPath))
            DymolaPath = FindInstalledDymola(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), DateTime.Now.Year + 1);
    }

    /// <summary>
    /// The install folders auto-detection looks for, newest release first: for each year from
    /// <paramref name="newestYear"/> back to <see cref="OldestDetectedYear"/>, <c>Dymola {year}x
    /// Refresh 1</c>, <c>Dymola {year}x</c> and <c>Dymola {year}</c>.
    /// </summary>
    /// <remarks>
    /// <para>That is also the order they were released in: the spring release of a year (<c>Dymola
    /// 2023</c>) came out before its autumn one (<c>2023x</c>), which came before the next spring's
    /// refresh (<c>2023x Refresh 1</c>) - and the autumn release named for the next year
    /// (<c>2024x</c>) is newer than all three.</para>
    ///
    /// <para><b>The spring releases used to be missed</b> - only the two <c>x</c> names were probed,
    /// so a machine with <c>Dymola 2022</c> or <c>Dymola 2023</c> and nothing later was found to have
    /// none - and the loop decremented the year before building the refresh name, so it probed
    /// <c>Dymola 2020x Refresh 1</c> while the documentation said 2021 onwards (B337).</para>
    /// </remarks>
    public static IEnumerable<string> CandidateInstallNames(int newestYear)
    {
        for (var year = newestYear; year >= OldestDetectedYear; year--)
        {
            yield return $"Dymola {year}x Refresh 1";
            yield return $"Dymola {year}x";
            yield return $"Dymola {year}";
        }
    }

    /// <summary>
    /// The newest <c>bin64/dymola.exe</c> under <paramref name="programFiles"/>, among
    /// <see cref="CandidateInstallNames"/>; empty when there is none.
    /// </summary>
    public static string FindInstalledDymola(string programFiles, int newestYear)
    {
        foreach (var name in CandidateInstallNames(newestYear))
        {
            var path = Path.Combine(programFiles, name, "bin64", "dymola.exe");
            if (File.Exists(path))
                return path;
        }

        return string.Empty;
    }
}
