using MLQT.Cli;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// What `mlqt` does with its command line before any checking happens.
///
/// <para>These are the paths a CI job hits when its invocation is wrong, and the exit code is the only
/// thing the job reads. A usage mistake has to be distinguishable from a failed quality gate, or a
/// pipeline reports a typo as a code problem — hence 2 for setup, 1 for the gate.</para>
/// </summary>
public class CliEntryTests
{
    private static async Task<(int Code, string Out, string Err)> RunAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CliEntry.RunAsync(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task NoArguments_ShowsUsageAndFailsAsSetup()
    {
        var (code, stdout, stderr) = await RunAsync();

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("Usage:", stderr);
        Assert.Empty(stdout);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public async Task AskingForHelp_Succeeds(string arg)
    {
        // Asked for deliberately, so it goes to stdout and exits 0 — a pipeline that runs `mlqt --help`
        // as a smoke test should not see a failure.
        var (code, stdout, stderr) = await RunAsync(arg);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("mlqt check <library-path>", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task TheVersion_GoesToStdout()
    {
        var (code, stdout, _) = await RunAsync("--version");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.StartsWith("mlqt ", stdout.Trim());
    }

    [Fact]
    public async Task AnUnknownCommand_NamesItAndShowsUsage()
    {
        var (code, stdout, stderr) = await RunAsync("chekc", "somewhere");

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("unknown command 'chekc'", stderr);
        Assert.Contains("Usage:", stderr);
        Assert.Empty(stdout);
    }

    [Fact]
    public async Task CheckWithoutALibraryPath_IsAUsageError()
    {
        var (code, _, stderr) = await RunAsync("check");

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("error:", stderr);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task CheckWithAnUnknownOption_IsAUsageErrorRatherThanIgnored()
    {
        // Silently ignoring it would run a check the user did not ask for and report a pass.
        var (code, _, stderr) = await RunAsync("check", ".", "--not-an-option");

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("error:", stderr);
    }

    [Fact]
    public async Task CheckOnAPathThatDoesNotExist_IsASetupErrorNotAGateFailure()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"mlqt-missing-{Guid.NewGuid():N}");

        var (code, _, stderr) = await RunAsync("check", missing);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("library path not found", stderr);
    }

    [Fact]
    public async Task BaselineWithNoSubcommand_IsAUsageError()
    {
        var (code, _, stderr) = await RunAsync("baseline");

        Assert.Equal(ExitCodes.Error, code);
        Assert.NotEqual(string.Empty, stderr);
    }
}
