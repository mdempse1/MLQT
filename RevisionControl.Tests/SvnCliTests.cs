namespace RevisionControl.Tests;

/// <summary>
/// Unit tests for the internal <see cref="SvnCli"/> helper. These cover the
/// pure pieces that do not spawn an svn process — revision normalisation, the
/// <see cref="SvnCli.Result"/> success/EnsureSuccess contract, and the
/// <see cref="SvnCliException"/> message shape. The process-spawning members
/// (Run/RunXml) are exercised by the integration tests against a real svn
/// client.
/// </summary>
public class SvnCliTests
{
    // ─── NormalizeRevision ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeRevision_NullOrWhitespace_ReturnsHead(string? revision)
    {
        Assert.Equal("HEAD", SvnCli.NormalizeRevision(revision));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("12345", "12345")]
    [InlineData("-7", "-7")] // long.TryParse accepts a leading minus; passes through verbatim
    public void NormalizeRevision_Numeric_PassesThrough(string revision, string expected)
    {
        Assert.Equal(expected, SvnCli.NormalizeRevision(revision));
    }

    [Theory]
    [InlineData("HEAD", "HEAD")]
    [InlineData("BASE", "BASE")]
    [InlineData("COMMITTED", "COMMITTED")]
    [InlineData("PREV", "PREV")]
    public void NormalizeRevision_KnownKeyword_PassesThroughUnchanged(string revision, string expected)
    {
        Assert.Equal(expected, SvnCli.NormalizeRevision(revision));
    }

    [Theory]
    [InlineData("head")]
    [InlineData("Base")]
    [InlineData("committed")]
    [InlineData("prev")]
    public void NormalizeRevision_KeywordIsCaseInsensitive_UppercasesToCanonicalForm(string revision)
    {
        Assert.Equal(revision.ToUpperInvariant(), SvnCli.NormalizeRevision(revision));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("trunk")]
    [InlineData("1.2.3")]   // not a long; not a keyword
    [InlineData("12a")]     // not parseable as a long
    public void NormalizeRevision_UnknownNonNumeric_FallsBackToHead(string revision)
    {
        Assert.Equal("HEAD", SvnCli.NormalizeRevision(revision));
    }

    // ─── Result ──────────────────────────────────────────────────────────────

    [Fact]
    public void Result_ExitCodeZero_IsSuccess()
    {
        var result = new SvnCli.Result { ExitCode = 0, StdOut = "ok", StdErr = "" };
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(255)]
    public void Result_NonZeroExitCode_IsNotSuccess(int exitCode)
    {
        var result = new SvnCli.Result { ExitCode = exitCode, StdOut = "", StdErr = "boom" };
        Assert.False(result.Success);
    }

    [Fact]
    public void EnsureSuccess_OnSuccess_ReturnsSameResultForChaining()
    {
        var result = new SvnCli.Result { ExitCode = 0, StdOut = "data", StdErr = "" };

        var chained = result.EnsureSuccess("info");

        Assert.Same(result, chained);
    }

    [Fact]
    public void EnsureSuccess_OnFailure_ThrowsSvnCliExceptionCarryingExitCodeAndStdErr()
    {
        var result = new SvnCli.Result { ExitCode = 42, StdOut = "", StdErr = "  path not found  " };

        var ex = Assert.Throws<SvnCliException>(() => result.EnsureSuccess("checkout"));

        Assert.Equal(42, ex.ExitCode);
        Assert.Equal("  path not found  ", ex.StdErr);
        // Message includes the operation, the exit code, and the trimmed stderr.
        Assert.Contains("checkout", ex.Message);
        Assert.Contains("42", ex.Message);
        Assert.Contains("path not found", ex.Message);
        Assert.DoesNotContain("  path not found  ", ex.Message); // stderr is trimmed in the message
    }

    // B329: svn's own message is the one that says what to do, and update, switch and
    // create-branch used to replace it with a bare "failed".

    [Fact]
    public void FailureMessage_IsSvnsOwnMessage_Trimmed()
    {
        var result = new SvnCli.Result
        {
            ExitCode = 1, StdOut = "",
            StdErr = "  svn: E155004: Run 'svn cleanup' to remove locks (type 'svn help cleanup' for details)\r\n",
        };

        Assert.Equal("svn: E155004: Run 'svn cleanup' to remove locks (type 'svn help cleanup' for details)",
            result.FailureMessage("SVN update failed."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \r\n")]
    public void FailureMessage_WithNothingFromSvn_IsTheFallback(string stderr)
    {
        var result = new SvnCli.Result { ExitCode = 1, StdOut = "", StdErr = stderr };

        Assert.Equal("SVN update failed.", result.FailureMessage("SVN update failed."));
    }

    // B330: a stalled svn is killed, which leaves the working copy locked (E155004) for every
    // command after it. A command on a working copy that had to be stopped is followed by
    // `svn cleanup`, and the message says what happened - or what the user must do.

    private const string WorkingCopy = @"C:\wc\Lib";

    private static SvnCli.Result Stopped() => new()
    {
        ExitCode = -1, StdOut = "", Stopped = true,
        StdErr = "svn produced no output for 10 minutes and was stopped.",
    };

    [Fact]
    public void RunOnWorkingCopy_AfterAStop_CleansTheWorkingCopyUp()
    {
        var calls = new List<string[]>();
        var result = SvnCli.RunOnWorkingCopy(args =>
        {
            calls.Add(args);
            return args[0] == "cleanup"
                ? new SvnCli.Result { ExitCode = 0, StdOut = "", StdErr = "" }
                : Stopped();
        }, WorkingCopy, ["update", "-r", "HEAD", WorkingCopy]);

        Assert.Equal(["cleanup", WorkingCopy], calls[1]);
        Assert.False(result.Success);
        Assert.True(result.Stopped);
        Assert.Contains("was stopped", result.StdErr);
        Assert.Contains("released with 'svn cleanup'", result.FailureMessage("SVN update failed."));
    }

    [Fact]
    public void RunOnWorkingCopy_WhenTheCleanupFailsToo_TellsTheUserWhatToRun()
    {
        var result = SvnCli.RunOnWorkingCopy(args => args[0] == "cleanup"
                ? new SvnCli.Result { ExitCode = 1, StdOut = "", StdErr = "svn: E155037: Previous operation has not finished" }
                : Stopped(),
            WorkingCopy, ["switch", "^/branches/x", WorkingCopy]);

        var message = result.FailureMessage("SVN switch failed.");
        Assert.Contains($"Run 'svn cleanup' on {WorkingCopy}", message);
        Assert.Contains("E155037", message);
    }

    [Fact]
    public void RunOnWorkingCopy_WithoutAStop_RunsNothingElse()
    {
        // A failure svn reported itself has already released its lock; cleaning up after it would
        // only cost time, and after a success it would be absurd.
        var calls = 0;
        var failed = new SvnCli.Result { ExitCode = 1, StdOut = "", StdErr = "svn: E170013: Unable to connect" };

        var result = SvnCli.RunOnWorkingCopy(_ => { calls++; return failed; }, WorkingCopy, ["update", WorkingCopy]);

        Assert.Equal(1, calls);
        Assert.Same(failed, result);
    }

    // ─── SvnCliException ─────────────────────────────────────────────────────

    [Fact]
    public void SvnCliException_FormatsMessageFromOperationExitCodeAndStdErr()
    {
        var ex = new SvnCliException("update", 7, "E160028: out of date");

        Assert.Equal(7, ex.ExitCode);
        Assert.Equal("E160028: out of date", ex.StdErr);
        Assert.Equal("svn update failed (exit 7): E160028: out of date", ex.Message);
    }
}
