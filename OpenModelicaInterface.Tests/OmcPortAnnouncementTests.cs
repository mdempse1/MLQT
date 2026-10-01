using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Finding where an omc started without a fixed port is listening, without an omc: what it prints,
/// and the port file it writes. The live half is <see cref="PortDiscoveryTests"/>.
/// </summary>
public sealed class OmcPortAnnouncementTests : IDisposable
{
    private const string Suffix = "mlqt-0123456789abcdef";

    /// <summary>What omc 1.27.1 printed on Windows, verbatim - no newline after the path.</summary>
    private const string Announced =
        "Created ZeroMQ Server.\nDumped server port in file: C:/Users/me/AppData/Local/Temp//openmodelica.port." + Suffix;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mlqt-omc-port-{Guid.NewGuid():N}");

    public OmcPortAnnouncementTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void ThePathIsTakenWithoutANewlineAfterIt()
    {
        Assert.Equal("C:/Users/me/AppData/Local/Temp//openmodelica.port." + Suffix,
            OmcPortAnnouncement.PortFilePath(Announced, Suffix));
    }

    [Fact]
    public void ThePathEndsAtTheLineWhenMoreFollows()
    {
        Assert.Equal("/tmp/openmodelica.me.port." + Suffix,
            OmcPortAnnouncement.PortFilePath(
                $"Dumped server port in file: /tmp/openmodelica.me.port.{Suffix}\r\nsomething else", Suffix));
    }

    [Fact]
    public void APathWithSpacesIsKeptWhole()
    {
        Assert.Equal("C:/Users/Jo Smith/Temp/openmodelica.port." + Suffix,
            OmcPortAnnouncement.PortFilePath(
                $"Dumped server port in file: C:/Users/Jo Smith/Temp/openmodelica.port.{Suffix}", Suffix));
    }

    [Theory]
    [InlineData("Created ZeroMQ Server.\n")]
    [InlineData("Created ZeroMQ Server.\nDumped server port in file: ")]
    // Part of the path: a read cut in the middle of the suffix is not the whole of it.
    [InlineData("Dumped server port in file: C:/Temp/openmodelica.port.mlqt-01234")]
    // The file an omc started without -z writes, which any other omc may have left behind.
    [InlineData("Dumped server port in file: C:/Temp/openmodelica.port")]
    public void NothingIsTakenUntilThePathIsWhole(string output)
    {
        Assert.Null(OmcPortAnnouncement.PortFilePath(output, Suffix));
    }

    [Fact]
    public async Task TheAnnouncementMayArriveInPieces()
    {
        var announcement = new OmcPortAnnouncement(Suffix);
        var split = Announced.Length - 7;

        announcement.Output(Announced[..split]);
        Assert.False(announcement.PortFile.IsCompleted);

        announcement.Output(Announced[split..]);
        Assert.EndsWith(Suffix, await announcement.PortFile);
    }

    /// <summary>
    /// A fixed port already taken: omc says so - on whichever stream - and exits at once, so the wait
    /// ends then and with what it said, not at the start-up limit with "did not answer".
    /// </summary>
    [Fact]
    public async Task ExitingFirst_SaysWhatOmcSaid()
    {
        var announcement = new OmcPortAnnouncement(Suffix);
        announcement.Error("Error creating ZeroMQ Server. zmq_bind failed: address in use\nOpenModelica Compiler v1.27.1\n");
        announcement.Ended();

        var failure = await Assert.ThrowsAsync<OpenModelicaExitedException>(() => announcement.PortFile);
        Assert.Contains("zmq_bind failed: address in use", failure.Message);
        Assert.DoesNotContain("OpenModelica Compiler", failure.Message);
    }

    [Fact]
    public async Task ExitingFirst_FallsBackToStdout_ThenToNothing()
    {
        var withOutput = new OmcPortAnnouncement(Suffix);
        withOutput.Output("\nError creating ZeroMQ Server.\n");
        withOutput.Ended();
        Assert.Contains("Error creating ZeroMQ Server.",
            (await Assert.ThrowsAsync<OpenModelicaExitedException>(() => withOutput.PortFile)).Message);

        var silent = new OmcPortAnnouncement(Suffix);
        silent.Ended();
        Assert.Equal("OpenModelica exited before it started listening.",
            (await Assert.ThrowsAsync<OpenModelicaExitedException>(() => silent.PortFile)).Message);
    }

    [Fact]
    public async Task OnceAnnounced_LaterOutputAndTheEndChangeNothing()
    {
        var announcement = new OmcPortAnnouncement(Suffix);
        announcement.Output(Announced);
        announcement.Output("Dumped server port in file: /elsewhere/" + Suffix);
        announcement.Ended();

        Assert.StartsWith("C:/Users/me", await announcement.PortFile);
    }

    [Theory]
    [InlineData("tcp://127.0.0.1:58553", 58553)]
    [InlineData("tcp://127.0.0.1:1", 1)]
    [InlineData("tcp://*:65535", 65535)]
    [InlineData("tcp://127.0.0.1", null)]
    [InlineData("tcp://127.0.0.1:0", null)]
    [InlineData("tcp://127.0.0.1:65536", null)]
    [InlineData("tcp://127.0.0.1:-1", null)]
    [InlineData("tcp://127.0.0.1:12ab", null)]
    [InlineData("tcp://:13027", null)]
    [InlineData("ipc://127.0.0.1:13027", null)]
    [InlineData("", null)]
    public void PortOf(string endpoint, int? port)
    {
        Assert.Equal(port, OmcPortAnnouncement.PortOf(endpoint));
    }

    [Fact]
    public void ThePortFileIsReadAndRemoved()
    {
        var path = Path.Combine(_folder, "openmodelica.port." + Suffix);
        File.WriteAllText(path, "tcp://127.0.0.1:58553");   // as omc writes it, no newline

        Assert.Equal("tcp://127.0.0.1:58553", OpenModelicaInterface.ReadPortFile(path));
        Assert.False(File.Exists(path), "omc leaves its port file behind; nothing would ever read it again");
    }

    [Fact]
    public void APortFileWithNoAddress_SaysWhatItHeld()
    {
        var path = Path.Combine(_folder, "openmodelica.port." + Suffix);
        File.WriteAllText(path, "garbage\n");

        var failure = Assert.Throws<InvalidOperationException>(() => OpenModelicaInterface.ReadPortFile(path));
        Assert.Contains("'garbage'", failure.Message);
    }

    [Fact]
    public void AMissingPortFile_IsReportedAsSuch()
    {
        var path = Path.Combine(_folder, "missing");

        var failure = Assert.Throws<InvalidOperationException>(() => OpenModelicaInterface.ReadPortFile(path));
        Assert.Contains(path, failure.Message);
    }

    [Fact]
    public void AnyPort_LeavesThePortToOmc_AndAFixedOneIsPassedOn()
    {
        Assert.Equal("--interactive=zmq -z=mlqt-x", OpenModelicaInterface.StartArguments("mlqt-x", OpenModelicaInterface.AnyPort));
        Assert.Equal("--interactive=zmq -z=mlqt-x --interactivePort=13027", OpenModelicaInterface.StartArguments("mlqt-x", 13027));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void APortOutOfRange_IsRefused(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenModelicaInterface("omc", port));
    }

    [Fact]
    public void ASessionNotStarted_HasNoPort()
    {
        using var omc = new OpenModelicaInterface("omc", 13027);
        Assert.Null(omc.Port);
    }
}
