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
        var exe = RequireSvn();

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinText != null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        // Global option; svn accepts it after positional arguments. Appending keeps the
        // caller's argument list focused on the subcommand and its operands.
        psi.ArgumentList.Add("--non-interactive");

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
            // Read both streams concurrently to avoid a pipe-buffer deadlock on large output
            // (e.g. `svn log` over thousands of revisions, or `svn status` on a big wc).
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (stdinText != null)
            {
                process.StandardInput.Write(stdinText);
                process.StandardInput.Close();
            }

            process.WaitForExit();
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            return new Result { ExitCode = process.ExitCode, StdOut = stdout, StdErr = stderr };
        }
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
