using DymolaInterface.Interfaces;
using Moq;
using OpenModelicaInterface.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// B493 - what MLQT exiting does to the two simulation tools: OpenModelica's session is ended and
/// Dymola's is let go of, never ended. The user decided the asymmetry: omc is headless and would run
/// on unseen, while the user may carry on working in a Dymola MLQT opened.
/// </summary>
/// <remarks>
/// That the Dymola factory's <c>Shutdown</c> really leaves a running process alone is held by
/// <see cref="DymolaLauncherStopTests.ExitingMlqt_LeavesWhatItStartedRunning"/>, and that
/// OpenModelica's ends omc's whole process tree by <c>OpenModelicaInterface.Tests/SessionEndTests</c>.
/// </remarks>
public class ExternalToolShutdownTests
{
    private readonly Mock<IOpenModelicaInterfaceFactory> _openModelica = new();
    private readonly Mock<IDymolaInterfaceFactory> _dymola = new();

    private ExternalToolShutdown Shutdown() => new(_openModelica.Object, _dymola.Object);

    [Fact]
    public void Exiting_EndsOpenModelica_AndLetsGoOfDymola()
    {
        var shutdown = Shutdown();

        shutdown.Run("a test");

        _openModelica.Verify(f => f.Shutdown(), Times.Once);
        _dymola.Verify(f => f.Shutdown(), Times.Once);
        Assert.True(shutdown.HasRun);
    }

    /// <summary>
    /// "MLQT is exiting" is not "stop this session": the requests that end a session are never made
    /// of either factory here, and for Dymola they would close the user's window.
    /// </summary>
    [Fact]
    public void Exiting_NeverResetsEitherSession()
    {
        Shutdown().Run("a test");

        _dymola.Verify(f => f.ResetAsync(), Times.Never);
        _dymola.Verify(f => f.GetOrCreateAsync(It.IsAny<CancellationToken>()), Times.Never);
        _openModelica.Verify(f => f.GetOrCreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Every way out calls it - the window closing, the process exiting, an unhandled exception - and
    /// an ordinary close reaches two of them. The work is done once.
    /// </summary>
    [Fact]
    public void EachWayOut_CanCallIt_AndTheWorkIsDoneOnce()
    {
        var shutdown = Shutdown();

        shutdown.Run("the window closed");
        shutdown.Run("the process is exiting");
        shutdown.Run("an unhandled exception");

        _openModelica.Verify(f => f.Shutdown(), Times.Once);
        _dymola.Verify(f => f.Shutdown(), Times.Once);
    }

    [Fact]
    public void NothingIsDone_UntilMlqtExits()
    {
        var shutdown = Shutdown();

        Assert.False(shutdown.HasRun);
        _openModelica.Verify(f => f.Shutdown(), Times.Never);
        _dymola.Verify(f => f.Shutdown(), Times.Never);
    }

    /// <summary>
    /// It runs on the way out, where an exception is a crash with nothing left to report it - and a
    /// failure to end one tool must not stop the other being dealt with.
    /// </summary>
    [Fact]
    public void AFailureWithOneTool_NeitherThrows_NorStopsTheOther()
    {
        _openModelica.Setup(f => f.Shutdown()).Throws(new InvalidOperationException("omc would not go"));
        _dymola.Setup(f => f.Shutdown()).Throws(new InvalidOperationException("nor would this"));

        Shutdown().Run("a test");

        _openModelica.Verify(f => f.Shutdown(), Times.Once);
        _dymola.Verify(f => f.Shutdown(), Times.Once);
    }

    /// <summary>
    /// Hooking the process's ways out does no work by itself - the tools are dealt with only when
    /// one of them happens.
    /// </summary>
    [Fact]
    public void HookingTheWaysOut_EndsNothingYet()
    {
        var shutdown = Shutdown();

        shutdown.EndWith(AppDomain.CurrentDomain);

        Assert.False(shutdown.HasRun);
        _openModelica.Verify(f => f.Shutdown(), Times.Never);
    }
}
