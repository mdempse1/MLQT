using OpenModelicaInterface;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// B260 — the OpenModelica session MLQT started ends when MLQT does.
///
/// <para><b>omc is headless.</b> An <c>omc</c> process started for a model check has no window and
/// no owner, so closing MLQT left it running with nothing to say what it was or that it should be
/// ended — it is found later in a task manager, if at all.</para>
///
/// <para><b>Dymola is deliberately left alone</b>, and that asymmetry is asserted here as well. Its
/// window is visible, the user may have carried on working in the session MLQT started, and closing
/// it from underneath them would lose that work. What makes the OpenModelica process worth ending
/// is exactly what makes it invisible.</para>
///
/// <para>The shutdown itself is one line in the host, which no other test reaches: the process it
/// ends cannot be started without OpenModelica installed. So this asks the two questions that are
/// true regardless — that the factory can end a session, and that the host asks it to.</para>
/// </summary>
public class OpenModelicaShutdownTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    private static string HostSource() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "MLQT.Photino", "Program.cs"));

    /// <summary>
    /// Disposing the factory is what ends the session, so the factory has to be disposable at all.
    /// It is registered as a singleton, so nothing else was ever going to dispose it.
    /// </summary>
    [Fact]
    public void TheFactoryCanEndItsSession()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(OpenModelicaInterfaceFactory)));
    }

    /// <summary>
    /// The ordinary case: MLQT closes having never checked a model, so there is no session to end.
    /// Shutdown must not throw over it — this runs after the window has gone, where an exception is
    /// a crash on exit with nothing left to report it.
    /// </summary>
    [Fact]
    public void EndingASessionThatWasNeverStartedIsSafe()
    {
        var factory = new OpenModelicaInterfaceFactory();

        factory.Dispose();
    }

    /// <summary>
    /// The host has to actually ask, and the call is a single line that reads as removable.
    /// </summary>
    [Fact]
    public void TheHostEndsTheSessionOnTheWayOut()
    {
        var source = HostSource();

        var run = source.IndexOf("app.Run();", StringComparison.Ordinal);
        // The call, not the method: "ShutDownOpenModelica(" alone matches the declaration below
        // it, so deleting the call left this green - found by deleting it.
        var shutdown = source.IndexOf("ShutDownOpenModelica(app.Services)", StringComparison.Ordinal);

        Assert.True(run >= 0, "the host no longer runs the application");
        Assert.True(shutdown > run,
            "MLQT.Photino/Program.cs must end the OpenModelica session after app.Run() returns");
    }

    /// <summary>
    /// And must not do the same to Dymola. Stated as a test because it is a decision, not an
    /// oversight: the obvious tidy-up here is to treat the two tools alike, and doing so would close
    /// a window the user may be working in.
    /// </summary>
    [Fact]
    public void TheHostLeavesDymolaAlone()
    {
        Assert.DoesNotContain("IDymolaInterfaceFactory", HostSource());
    }
}
