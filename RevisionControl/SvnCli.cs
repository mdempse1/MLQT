using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace RevisionControl;

/// <summary>
/// Thin wrapper around the <c>svn</c> command-line client. This is the single point
/// through which <see cref="SvnRevisionControlSystem"/> talks to Subversion — the
/// managed SharpSvn library has been removed in favour of the (much faster) CLI, and
/// the executable is located by <see cref="SvnToolLocator"/> (bundled SlikSVN client,
/// then MLQT_SVN_PATH, then svn on PATH).
///
/// Arguments are passed via <see cref="ProcessStartInfo.ArgumentList"/> so the runtime
/// handles quoting/escaping — callers never build a single command string and never
/// need to quote paths themselves. <c>--non-interactive</c> is appended to every
/// invocation so the client never blocks waiting for a prompt (auth, conflict, etc.).
/// </summary>
internal static class SvnCli
{
    /// <summary>Result of a single svn invocation.</summary>
    internal sealed class Result
    {
        public required int ExitCode { get; init; }
        public required string StdOut { get; init; }
        public required string StdErr { get; init; }
        public bool Success => ExitCode == 0;

        /// <summary>
        /// Throws an <see cref="SvnCliException"/> when the command failed. Returns this
        /// result otherwise so calls can be chained: <c>SvnCli.Run(...).EnsureSuccess()</c>.
        /// </summary>
        public Result EnsureSuccess(string operation)
        {
            if (!Success)
                throw new SvnCliException(operation, ExitCode, StdErr);
            return this;
        }
    }

    /// <summary>Result of an svn invocation whose output is a file rather than text (B264).</summary>
    internal sealed class BytesResult
    {
        public required int ExitCode { get; init; }
        public required byte[] StdOut { get; init; }
        public required string StdErr { get; init; }
        public bool Success => ExitCode == 0;
    }

    /// <summary>
    /// Resolves the svn executable or throws. Once SharpSvn was removed, svn became a
    /// hard requirement; shipped builds carry the bundled SlikSVN client, and developer
    /// machines need svn on PATH (or MLQT_SVN_PATH set).
    /// </summary>
    private static string RequireSvn()
    {
        var exe = SvnToolLocator.SvnExecutablePath;
        if (exe == null)
        {
            throw new SvnCliException(
                "locate-svn", -1,
                "No svn client found. Set the MLQT_SVN_PATH environment variable, bundle the " +
                "SlikSVN client under the app's svn/ folder, or install svn on your PATH.");
        }
        return exe;
    }

    /// <summary>
    /// How long svn may go without writing anything before it is taken to have stalled and is
    /// stopped (B297).
    /// </summary>
    /// <remarks>
    /// Silence, not running time. A checkout or update of a large repository can legitimately take
    /// longer than any fixed limit, but svn reports each file as it goes, so a command that is still
    /// working is a command that is still writing. What this catches is a server that accepted the
    /// connection and then stopped answering - which used to block update, commit, switch or merge,
    /// and the dialog waiting on it, for good.
    /// </remarks>
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Runs svn and returns standard output as the bytes svn wrote, not as text.
    /// </summary>
    /// <remarks>
    /// <para>For <c>svn cat</c>, and for nothing else. Every other command writes text svn generates
    /// itself - XML, status codes, revision numbers - which is UTF-8 by definition, so decoding it is
    /// right. <c>cat</c> writes a <b>file</b>, and its encoding is the file's own business: a
    /// Windows-1252 Modelica library decoded as UTF-8 comes back with replacement characters where
    /// its accented characters were, and by then the bytes are gone (B264).</para>
    ///
    /// <para>stderr is still text, because svn wrote it.</para>
    /// </remarks>
    internal static BytesResult RunForBytes(params string[] args)
    {
        var raw = Execute(RequireSvn(), WithNonInteractive(args), stdinText: null, IdleTimeout);
        return new BytesResult { ExitCode = raw.ExitCode, StdOut = raw.StdOut, StdErr = raw.StdErr };
    }

    /// <summary>
    /// Runs <c>svn &lt;args...&gt; --non-interactive</c> and returns its exit code and
    /// captured output. Never throws on a non-zero exit code (inspect <see cref="Result.Success"/>);
    /// it only throws if no svn executable can be found or the process cannot be started.
    /// </summary>
    internal static Result Run(params string[] args) => Run((IEnumerable<string>)args, stdinText: null);

    /// <summary>
    /// Runs svn with an explicit argument list and, optionally, text piped to stdin.
    /// </summary>
    internal static Result Run(IEnumerable<string> args, string? stdinText = null)
    {
        var raw = Execute(RequireSvn(), WithNonInteractive(args), stdinText, IdleTimeout);
        return new Result { ExitCode = raw.ExitCode, StdOut = Decode(raw.StdOut), StdErr = raw.StdErr };
    }

    // Global option; svn accepts it after positional arguments. Appending keeps the caller's
    // argument list focused on the subcommand and its operands.
    private static IEnumerable<string> WithNonInteractive(IEnumerable<string> args) =>
        args.Append("--non-interactive");

    /// <summary>What <see cref="Execute"/> captured: stdout as bytes, for the caller to decode or not.</summary>
    internal sealed record RawResult(int ExitCode, byte[] StdOut, string StdErr);

    /// <summary>
    /// Runs a command to completion, and never waits on something that will not come (B297).
    /// </summary>
    /// <remarks>
    /// <para><b>Both streams are read at once</b>, or output larger than the pipe buffer deadlocks -
    /// <c>svn log</c> over thousands of revisions, <c>svn status</c> on a big working copy, a large
    /// file from <c>svn cat</c>. Read as bytes, so <c>cat</c> can keep them (B264) and so every read
    /// counts as a sign of life.</para>
    ///
    /// <para><b>Stdin is always redirected and closed</b>, after <paramref name="stdinText"/> if
    /// there is any. It used to be inherited whenever nothing was piped in.</para>
    ///
    /// <para><b>A command silent for <paramref name="idleLimit"/> is stopped</b>, with everything it
    /// started, and reported as a failure that says so. Takes the executable rather than finding svn
    /// itself so a test can drive it with something that stalls on purpose.</para>
    /// </remarks>
    internal static RawResult Execute(string exe, IEnumerable<string> args, string? stdinText, TimeSpan idleLimit)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Win32Exception ex)
        {
            throw new SvnCliException("start-svn", -1,
                $"Failed to start svn executable '{exe}': {ex.Message}");
        }

        using (process)
        {
            long lastActivity = Environment.TickCount64;
            void Touched() => Interlocked.Exchange(ref lastActivity, Environment.TickCount64);

            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();
            var stdoutTask = Drain(process.StandardOutput.BaseStream, stdout, Touched);
            var stderrTask = Drain(process.StandardError.BaseStream, stderr, Touched);

            try
            {
                if (stdinText != null)
                    process.StandardInput.Write(stdinText);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // It exited without reading its input; the exit code says what happened.
            }

            while (!process.WaitForExit(250))
            {
                if (Environment.TickCount64 - Interlocked.Read(ref lastActivity) < idleLimit.TotalMilliseconds)
                    continue;

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It finished between the check and the kill.
                }

                // Bounded: a descendant that escaped the kill can hold the pipes open.
                Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(5));
                var said = stderrTask.IsCompleted ? Encoding.UTF8.GetString(stderr.ToArray()).Trim() : "";
                var message = $"svn produced no output for {idleLimit.TotalMinutes:0.##} minutes and was stopped.";
                return new RawResult(-1,
                    stdoutTask.IsCompleted ? stdout.ToArray() : [],
                    string.IsNullOrEmpty(said) ? message : $"{message}{Environment.NewLine}{said}");
            }

            // The overload without a limit also waits for the redirected streams to be drained.
            process.WaitForExit();
            stdoutTask.GetAwaiter().GetResult();
            stderrTask.GetAwaiter().GetResult();
            return new RawResult(process.ExitCode, stdout.ToArray(), Encoding.UTF8.GetString(stderr.ToArray()));
        }
    }

    private static async Task Drain(Stream source, MemoryStream into, Action onRead)
    {
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            into.Write(buffer, 0, read);
            onRead();
        }
    }

    // Decoded as a StreamReader decodes, which is what reading StandardOutput used to do - so a byte
    // order mark is taken as one rather than kept as a character.
    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Runs an svn subcommand with <c>--xml</c> appended and parses stdout into an
    /// <see cref="XDocument"/>. Returns null when the command fails (the caller decides
    /// whether that is an error or an expected "doesn't exist" outcome).
    /// </summary>
    internal static XDocument? RunXml(params string[] args)
    {
        var withXml = new List<string>(args) { "--xml" };
        var result = Run(withXml);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StdOut))
            return null;
        try
        {
            return XDocument.Parse(result.StdOut);
        }
        catch (System.Xml.XmlException ex)
        {
            RevisionControlLogger.Error("SvnCli.RunXml", ex);
            return null;
        }
    }

    /// <summary>
    /// Normalizes a revision identifier for the svn CLI. Empty/whitespace becomes HEAD;
    /// numeric revisions pass through; the SVN keywords (HEAD/BASE/COMMITTED/PREV) are
    /// upper-cased; anything else falls back to HEAD (matching the old SharpSvn behaviour).
    /// </summary>
    internal static string NormalizeRevision(string? revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
            return "HEAD";
        if (long.TryParse(revision, out _))
            return revision;
        return revision.ToUpperInvariant() switch
        {
            "HEAD" => "HEAD",
            "BASE" => "BASE",
            "COMMITTED" => "COMMITTED",
            "PREV" => "PREV",
            _ => "HEAD"
        };
    }
}

/// <summary>
/// Raised when an svn CLI invocation cannot be run or returns a non-zero exit code in a
/// context where success was required. Carries the stderr text so callers can surface a
/// meaningful message (and detect conditions such as "out of date").
/// </summary>
internal sealed class SvnCliException : Exception
{
    public int ExitCode { get; }
    public string StdErr { get; }

    public SvnCliException(string operation, int exitCode, string stderr)
        : base($"svn {operation} failed (exit {exitCode}): {stderr.Trim()}")
    {
        ExitCode = exitCode;
        StdErr = stderr;
    }
}
