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

    public DymolaSettings()
    {
        if (string.IsNullOrEmpty(DymolaPath)) {
            //Search for the most recent Dymola version
            var year = DateTime.Now.Year + 1;
            var refreshVersionNext = false;
            string versionName;
            while (year > 2020)
            {
                if (refreshVersionNext) 
                {
                    year--;
                    versionName = $"Dymola {year}x Refresh 1";
                }
                else
                    versionName = $"Dymola {year}x";
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), versionName, "bin64", "dymola.exe");
                if (File.Exists(path)) 
                {
                    DymolaPath = path;
                    break;
                }
                refreshVersionNext = !refreshVersionNext;
            }            
        }
    }
}