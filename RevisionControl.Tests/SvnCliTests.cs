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
