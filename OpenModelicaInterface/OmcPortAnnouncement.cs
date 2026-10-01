using System.Text;

namespace OpenModelicaInterface;

/// <summary>
/// Where an omc started with <c>--interactive=zmq -z=&lt;suffix&gt;</c> says it is listening.
/// </summary>
/// <remarks>
/// <para>omc binds whatever port it is given with <c>--interactivePort</c>, or any free one without
/// it, then writes the address it bound - <c>tcp://127.0.0.1:58553</c> - to a port file in the temp
/// directory and prints <c>Dumped server port in file: &lt;path&gt;</c> on stdout. The file is named
/// after the suffix (<c>openmodelica.port.&lt;suffix&gt;</c> on Windows,
/// <c>openmodelica.&lt;user&gt;.port.&lt;suffix&gt;</c> on Linux), so the path is taken from stdout
/// rather than rebuilt here.</para>
///
/// <para><b>omc prints the path with no newline after it</b>, so stdout is fed in here as it arrives
/// rather than read by line - a line reader waits for the end of a line omc never finishes. The path
/// is complete once it ends with the suffix, which is unique to the session: a prefix of it is never
/// mistaken for the whole, and a stale <c>openmodelica.port</c> from an omc started without a suffix
/// is never read.</para>
///
/// <para>An omc that cannot bind - a fixed port already taken - prints why and exits at once, so the
/// end of stdout before the announcement ends the wait with what it said rather than a time limit.</para>
/// </remarks>
internal sealed class OmcPortAnnouncement
{
    internal const string Marker = "Dumped server port in file:";

    /// <summary>More than omc prints before its announcement; bounds what an omc that prints its
    /// usage text instead is kept for.</summary>
    private const int MaxKept = 16 * 1024;

    private readonly object _gate = new();
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _errors = new();
    private readonly TaskCompletionSource<string> _portFile = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OmcPortAnnouncement(string suffix) => Suffix = suffix;

    /// <summary>The <c>-z</c> suffix this omc was started with.</summary>
    public string Suffix { get; }

    /// <summary>The port file's path, once omc has announced it; faults with
    /// <see cref="OpenModelicaExitedException"/> when omc's stdout ends first.</summary>
    public Task<string> PortFile => _portFile.Task;

    /// <summary>Text omc wrote to stdout.</summary>
    public void Output(string text)
    {
        lock (_gate)
        {
            if (_portFile.Task.IsCompleted)
                return;

            Keep(_output, text);
            if (PortFilePath(_output.ToString(), Suffix) is { } path)
                _portFile.TrySetResult(path);
        }
    }

    /// <summary>Text omc wrote to stderr - kept only to say why it exited, never searched for the
    /// announcement, so its writes cannot land in the middle of the path.</summary>
    public void Error(string text)
    {
        lock (_gate)
        {
            if (!_portFile.Task.IsCompleted)
                Keep(_errors, text);
        }
    }

    /// <summary>omc's stdout has ended: it has exited, and if it had not announced a port it never will.</summary>
    public void Ended()
    {
        lock (_gate)
        {
            if (_portFile.Task.IsCompleted)
                return;

            var said = FirstLine(_errors.ToString()) ?? FirstLine(_output.ToString());
            _portFile.TrySetException(new OpenModelicaExitedException(said is null
                ? "OpenModelica exited before it started listening."
                : $"OpenModelica exited before it started listening: {said}"));
        }
    }

    private static void Keep(StringBuilder kept, string text)
    {
        if (kept.Length < MaxKept)
            kept.Append(text, 0, Math.Min(text.Length, MaxKept - kept.Length));
    }

    /// <summary>
    /// The port file <paramref name="output"/> announces, or null while it has not been announced in
    /// full.
    /// </summary>
    internal static string? PortFilePath(string output, string suffix)
    {
        var at = output.IndexOf(Marker, StringComparison.Ordinal);
        if (at < 0)
            return null;

        var rest = output[(at + Marker.Length)..];
        var end = rest.IndexOfAny(['\r', '\n']);
        var path = (end < 0 ? rest : rest[..end]).Trim();

        return path.EndsWith(suffix, StringComparison.Ordinal) ? path : null;
    }

    /// <summary>The address in a port file's text, or null when it is not one: <c>tcp://host:port</c>.</summary>
    internal static string? Endpoint(string portFileText)
    {
        var endpoint = portFileText.Trim();
        return PortOf(endpoint) is not null ? endpoint : null;
    }

    /// <summary>The port in a <c>tcp://host:port</c> address, or null when it has none.</summary>
    internal static int? PortOf(string endpoint)
    {
        if (!endpoint.StartsWith("tcp://", StringComparison.Ordinal))
            return null;

        var colon = endpoint.LastIndexOf(':');
        return colon > "tcp://".Length
               && int.TryParse(endpoint.AsSpan(colon + 1), System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out var port)
               && port is > 0 and <= 65535
            ? port
            : null;
    }

    private static string? FirstLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
}
