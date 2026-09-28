using System.Runtime.InteropServices;
using DymolaInterface.Interfaces;
using OpenModelicaInterface.Interfaces;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <summary>
/// What MLQT does, as it exits, to the simulation tools it started: <b>OpenModelica is ended and
/// Dymola is left running</b> (B260, B493).
/// </summary>
/// <remarks>
/// <para><b>The asymmetry is the user's decision, and deliberate.</b> omc is headless: one MLQT
/// started for a model check has no window and no owner, so left running it is found later in a task
/// manager, if at all - it is ended, with every process it started. Dymola has a window, and the user
/// may carry on working in the one MLQT opened, so it is let go of and never killed, on Windows and
/// Linux alike. "MLQT is exiting" is therefore a different request from "stop this session", which
/// for Dymola does end the process tree (B411); each factory's <c>Shutdown</c> is the first, and
/// nothing on this path reaches the second.</para>
///
/// <para><b>One place, every way out.</b> A window closed ends <c>app.Run()</c>, and the host calls
/// <see cref="Run"/> after it. A process ended from outside - <c>SIGTERM</c> at logout or from
/// <c>kill</c>, <c>Environment.Exit</c> - never returns from <c>app.Run()</c>, and an unhandled
/// exception ends the process without running a <c>finally</c>; before B493 each of those left omc
/// running. <see cref="EndWith"/> hooks those, and <see cref="Run"/> does its work once whichever
/// arrives first. What no process can answer for is being killed outright (<c>SIGKILL</c>, Task
/// Manager's End task): nothing in MLQT runs then.</para>
///
/// <para>The MCP server needs none of this: it does not check models, and registers neither
/// factory.</para>
/// </remarks>
public sealed class ExternalToolShutdown(
    IOpenModelicaInterfaceFactory openModelica,
    IDymolaInterfaceFactory dymola)
{
    private int _ran;

    /// <summary>Kept so the registrations stay in force: disposing one withdraws it.</summary>
    private readonly List<PosixSignalRegistration> _signals = [];

    /// <summary>Whether <see cref="Run"/> has done its work.</summary>
    public bool HasRun => Volatile.Read(ref _ran) != 0;

    /// <summary>
    /// Ends the OpenModelica session and lets go of the Dymola one. The first call does the work and
    /// every later one returns at once, so each exit path can call it without asking the others.
    /// Never throws: it runs where an exception is a crash on the way out with nothing left to report
    /// it.
    /// </summary>
    /// <param name="why">How MLQT is exiting, for the log.</param>
    public void Run(string why)
    {
        if (Interlocked.Exchange(ref _ran, 1) != 0)
            return;

        Quietly(() => Info(nameof(ExternalToolShutdown),
            $"MLQT is exiting ({why}): ending the OpenModelica session, leaving Dymola running"));

        try
        {
            openModelica.Shutdown();
        }
        catch (Exception ex)
        {
            Quietly(() => Warn(nameof(ExternalToolShutdown), $"Could not end the OpenModelica session: {ex.Message}"));
        }

        try
        {
            dymola.Shutdown();
        }
        catch (Exception ex)
        {
            Quietly(() => Warn(nameof(ExternalToolShutdown), $"Could not let go of the Dymola session: {ex.Message}"));
        }
    }

    /// <summary>
    /// Runs <see cref="Run"/> on the ways out that never return from the host's run loop: the
    /// process exiting (<c>SIGTERM</c>, <c>Environment.Exit</c>), an unhandled exception, and on a
    /// terminal, <c>SIGINT</c> and <c>SIGHUP</c>. Call once, as the host starts.
    /// </summary>
    public void EndWith(AppDomain domain)
    {
        domain.ProcessExit += (_, _) => Run("the process is exiting");
        domain.UnhandledException += (_, _) => Run("an unhandled exception");

        // Ctrl+C in the terminal MLQT was started from, and that terminal closing. The default
        // action - ending the process - still follows; this only goes first.
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGHUP })
        {
            try
            {
                _signals.Add(PosixSignalRegistration.Create(signal, _ => Run($"{signal}")));
            }
            catch (PlatformNotSupportedException)
            {
                // Not every platform has every signal; the others still apply.
            }
        }
    }

    private static void Quietly(Action log)
    {
        // Logging may already have been shut down on the way out; it must not decide the exit.
        try { log(); } catch { /* nothing left to tell */ }
    }
}
