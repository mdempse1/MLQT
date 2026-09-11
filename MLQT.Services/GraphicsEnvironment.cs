namespace MLQT.Services;

/// <summary>
/// What the machine can do about drawing, recorded once at startup on Linux.
/// </summary>
/// <remarks>
/// <para>Backlog B141, and it exists to answer a question rather than to change anything. The Linux
/// machine every judgement in phase 7b was made on is a Hyper-V guest where Mesa cannot pick a
/// device — <c>libEGL warning: failed to get driver name for fd -1</c>, then
/// <c>MESA: error: ZINK: failed to choose pdev</c> — so WebKit falls back to software rendering and
/// every composite and paint lands on the CPU. That is harmless for correctness (7b-0 saw the same
/// warnings, and every self-test probe passes) and it is not harmless for smoothness, which is a
/// distinction nobody drew until a scrolling complaint needed explaining.</para>
///
/// <para><b>So "MLQT feels slow on Linux" is not answerable without knowing this</b>, and the place a
/// performance report is investigated from is the log. The alternative is what happened: measuring
/// layout in the engine (120 forced scroll-and-reflow steps over a 42,000-node diff take 6 ms, so the
/// DOM is not the cost), then working out from first principles that the machine had no GPU.</para>
///
/// <para>Nothing here changes behaviour, and none of it is a defect in MLQT. A DRM render node is
/// what Mesa needs to find a device; without one, hardware acceleration is not available whatever
/// else is configured. Its presence is not a promise that acceleration works — the card node existed
/// on the machine in B141 — which is why the wording below says what was found rather than what will
/// happen.</para>
/// </remarks>
public static class GraphicsEnvironment
{
    /// <summary>Where the kernel exposes direct-rendering devices.</summary>
    public const string RenderNodeDirectory = "/dev/dri";

    /// <summary>
    /// One line for the log, or null when there is nothing worth saying.
    /// </summary>
    /// <param name="isLinux">Whether this is the platform the question applies to.</param>
    /// <param name="sessionType">The <c>XDG_SESSION_TYPE</c> value: wayland, x11, tty, or null.</param>
    /// <param name="renderNodes">The <c>renderD*</c> entries found under <see cref="RenderNodeDirectory"/>.</param>
    /// <remarks>
    /// Pure, and takes the facts rather than gathering them, so the interesting case — a machine with
    /// no render node — can be tested from a machine that has one.
    ///
    /// <para>Null on Windows rather than a cheerful line saying everything is fine: a log line that
    /// appears on every run everywhere is one people learn to skip, and this one is meant to be
    /// noticed on the day somebody is reading the log to explain a slow window.</para>
    /// </remarks>
    public static string? Describe(bool isLinux, string? sessionType, IReadOnlyCollection<string> renderNodes)
    {
        if (!isLinux)
            return null;

        var session = string.IsNullOrWhiteSpace(sessionType) ? "unknown" : sessionType.Trim();

        if (renderNodes.Count == 0)
        {
            return $"Graphics: session={session}, no DRM render node under {RenderNodeDirectory}. "
                 + "Hardware acceleration is unavailable, so the webview paints on the CPU - treat any "
                 + "rendering-speed measurement from this machine as a lower bound (B141).";
        }

        return $"Graphics: session={session}, DRM render node(s): {string.Join(", ", renderNodes.Order(StringComparer.Ordinal))}.";
    }

    /// <summary>Asks the machine the three questions and returns the line, or null.</summary>
    /// <remarks>
    /// The I/O half, kept apart from the decision above. A directory that cannot be read is treated
    /// as having no render nodes, which is the same answer by a different route and is never worth
    /// failing a startup over.
    /// </remarks>
    public static string? Probe()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        string[] nodes;
        try
        {
            nodes = Directory.Exists(RenderNodeDirectory)
                ? Directory.GetFiles(RenderNodeDirectory, "renderD*").Select(Path.GetFileName).OfType<string>().ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            nodes = [];
        }

        return Describe(true, Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), nodes);
    }
}
