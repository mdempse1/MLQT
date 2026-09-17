using MLQT.McpTester.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// That <c>MLQT.McpTester</c> opens with a server path that could exist on this machine.
///
/// <para><b>What broke (B180).</b> The default was a literal path into one developer's checkout, so
/// the first Connect failed for everyone else. Nothing caught it because nothing runs that app — no
/// test and no CI job, which is the same gap B167 recorded.</para>
///
/// <para>The locator takes its base directory and its existence check as arguments precisely so this
/// can be asserted without a filesystem. The tests below use POSIX-shaped paths built with
/// <see cref="Path.Combine"/> so they read the same on both platforms.</para>
/// </summary>
public class McpServerLocatorTests
{
    private const string Exe = "MLQT.McpServer.exe";

    private static string BuildOutput(params string[] parts) => Path.Combine(parts);

    private static readonly string TesterBuildOutput =
        BuildOutput("C:", "src", "MLQT", "MLQT.McpTester", "bin", "Debug", "net10.0");

    private static readonly string SiblingServer =
        BuildOutput("C:", "src", "MLQT", "MLQT.McpServer", "bin", "Debug", "net10.0", Exe);

    [Fact]
    public void TheServerBesideTheTesterIsPreferred()
    {
        // The installer's layout: publish-tools.sh puts the shipping tools in one tree.
        var alongside = Path.Combine(TesterBuildOutput, Exe);

        var found = McpServerLocator.Locate(TesterBuildOutput, Exe, p => p == alongside);

        Assert.Equal(alongside, found);
    }

    [Fact]
    public void TheSiblingProjectsBuildOutputIsFoundWhenNothingSitsAlongside()
    {
        // The developer case, and the one the hard-coded path was serving.
        var found = McpServerLocator.Locate(TesterBuildOutput, Exe, p => p == SiblingServer);

        Assert.Equal(SiblingServer, found);
    }

    [Fact]
    public void TheConfigurationAndFrameworkAreCarriedAcrossRatherThanAssumed()
    {
        // A Release build must not be handed a Debug server. The tail is reused, so this needs no
        // edit when the framework moves on either.
        var releaseTester = BuildOutput("C:", "src", "MLQT", "MLQT.McpTester", "bin", "Release", "net11.0");
        var releaseServer = BuildOutput(
            "C:", "src", "MLQT", "MLQT.McpServer", "bin", "Release", "net11.0", Exe);

        var found = McpServerLocator.Locate(releaseTester, Exe, p => p == releaseServer);

        Assert.Equal(releaseServer, found);
    }

    [Fact]
    public void ATrailingSeparatorIsTolerated()
    {
        // AppContext.BaseDirectory always has one, so this is the real call shape rather than an
        // edge case. Without the trim every path segment below it reads as empty.
        var withSeparator = TesterBuildOutput + Path.DirectorySeparatorChar;

        var found = McpServerLocator.Locate(withSeparator, Exe, p => p == SiblingServer);

        Assert.Equal(SiblingServer, found);
    }

    [Fact]
    public void NothingFoundGivesAnEmptyStringRatherThanAGuess()
    {
        // The point of the change: an empty box is honest, a path that does not exist is not.
        var found = McpServerLocator.Locate(TesterBuildOutput, Exe, _ => false);

        Assert.Equal("", found);
    }

    [Fact]
    public void AnUnexpectedlyShallowBaseDirectoryDoesNotThrow()
    {
        // Single-file or otherwise relocated hosts can put the binary somewhere with no bin/cfg/tfm
        // tail to walk up. It should decline rather than fail.
        var found = McpServerLocator.Locate(
            Path.DirectorySeparatorChar.ToString(), Exe, _ => false);

        Assert.Equal("", found);
    }

    [Fact]
    public void TheExecutableNameFollowsThePlatform()
    {
        // The old literal ended in .exe, which is not what the apphost is called on Linux — and the
        // tester ships on both since 7b-1.
        var expected = OperatingSystem.IsWindows() ? "MLQT.McpServer.exe" : "MLQT.McpServer";

        Assert.Equal(expected, McpServerLocator.ExecutableName);
    }

    [Fact]
    public void NoCandidateIsOfferedForAnEmptyBaseDirectory()
    {
        Assert.Empty(McpServerLocator.CandidatePaths("", Exe));
        Assert.Empty(McpServerLocator.CandidatePaths(TesterBuildOutput, ""));
    }
}
