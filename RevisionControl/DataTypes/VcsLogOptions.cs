namespace RevisionControl;

/// <summary>
/// Options for retrieving log entries.
/// </summary>
public class VcsLogOptions
{
    /// <summary>
    /// Maximum number of entries to retrieve. Default is 50.
    /// </summary>
    public int MaxEntries { get; set; } = 50;

    /// <summary>
    /// Optional start date filter. Only entries on or after this date are included.
    /// </summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>
    /// Optional end date filter. Only entries on or before this date are included.
    /// </summary>
    public DateTimeOffset? Until { get; set; }

    /// <summary>
    /// Optional branch/path to filter on.
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Creates default options for retrieving log entries for the past week,
    /// but ensures at least the specified minimum number of entries.
    /// </summary>
    /// <param name="minEntries">Minimum number of entries to retrieve. Default is 10.</param>
    /// <returns>VcsLogOptions configured for past week with minimum entries.</returns>
    public static VcsLogOptions DefaultPastWeek(int minEntries = 10)
    {
        return new VcsLogOptions
        {
            MaxEntries = Math.Max(50, minEntries),
            Since = DateTimeOffset.Now.AddDays(-7)
        };
    }
}
