using System.Diagnostics;
using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// B493 - ending an omc session ends omc and <b>everything it started</b>, as MLQT exits. Needs no omc.
/// </summary>
/// <remarks>
/// <para>omc runs what a command asks for - a compiler, a simulation, a <c>system()</c> call - as
/// children, and a session is ended by force exactly when omc is too busy with one of those to
/// answer <c>quit()</c>. Disposing called <c>Kill()</c>, which ended omc and left the child: on Linux
/// it is handed to init and runs on, headless and unowned.</para>
///
/// <para><b>The fake omc</b> is the shape <c>DymolaLauncherStopTests</c> uses: a <c>.cmd</c> on Windows
/// and a <c>sh</c> script elsewhere, starting one long-running child in the foreground that writes its
/// own process id to a file. The session adopts the process through an internal constructor, as though
/// it had started it, and is not connected - so disposing it goes straight to ending the process.</para>
///
/// <para>No omc, so CI's tool-free run of this suite runs it, on Windows and Linux. The same promise
/// against a real omc is <see cref="LiveSessionEndTests"/>.</para>
/// </remarks>
public sealed class SessionEndTests : IDisposable
{
    private static readonly TimeSpan NeverThisLong = TimeSpan.FromSeconds(60);

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"mlqt-omc-tree-{Guid.NewGuid():N}");
    private readonly string _pidFile;
    private int? _childPid;
    private Process? _omc;

    public SessionEndTests()
    {
        Directory.CreateDirectory(_folder);
        _pidFile = Path.Combine(_folder, "child.pid");
    }

    public void Dispose()
    {
        // A failing run must not leave a ten-minute sleeper behind.
        if (_childPid is { } pid)
            try { using var child = Process.GetProcessById(pid); child.Kill(); } catch { /* gone */ }
        try { if (_omc is { HasExited: false }) _omc.Kill(entireProcessTree: true); } catch { /* gone */ }
        _omc?.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    private string WriteFakeOmc()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_folder, "omc.cmd");
            File.WriteAllText(path,
                "@echo off\r\n" +
                "powershell.exe -NoProfile -NonInteractive -Command " +
                $"\"[IO.File]::WriteAllText('{_pidFile}.tmp', $PID); " +
                $"Move-Item -Force '{_pidFile}.tmp' '{_pidFile}'; Start-Sleep -Seconds 600\"\r\n");
            return path;
        }

        var script = Path.Combine(_folder, "omc");
        File.WriteAllText(script,
            "#!/bin/sh\n" +
            $"sh -c 'echo $$ > \"{_pidFile}.tmp\"; mv \"{_pidFile}.tmp\" \"{_pidFile}\"; exec sleep 600'\n");
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    /// <summary>Starts the fake omc and returns once its child is running.</summary>
    private async Task<Process> StartFakeOmcWithAChildAsync()
    {
        _omc = Process.Start(new ProcessStartInfo(WriteFakeOmc())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("the fake omc did not start");

        var waited = Stopwatch.StartNew();
        while (!File.Exists(_pidFile))
        {
            Assert.False(_omc.HasExited, "the fake omc ended before its child was up");
            Assert.True(waited.Elapsed < NeverThisLong, "the fake omc's child never started");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        _childPid = int.Parse(File.ReadAllText(_pidFile).Trim());
        var child = Process.GetProcessById(_childPid.Value);
        Assert.False(child.HasExited);
        return child;
    }

    [Fact]
    public async Task EndingASession_EndsWhatOmcStarted()
    {
        using var child = await StartFakeOmcWithAChildAsync();
        // A handle of the test's own: the session disposes the one it was given.
        using var omc = Process.GetProcessById(_omc!.Id);
        var session = new OpenModelicaInterface(_omc);

        session.Dispose();

        Assert.True(omc.WaitForExit(NeverThisLong), "the session was ended and omc is still running");
        Assert.True(child.WaitForExit(NeverThisLong),
            "the session was ended and what omc started is still running");
    }

    /// <summary>
    /// The factory's exit path is the session's disposal: MLQT exiting ends the tree through it.
    /// </summary>
    [Fact]
    public async Task ShuttingTheFactoryDown_EndsTheSessionItHolds()
    {
        using var child = await StartFakeOmcWithAChildAsync();
        var factory = new OpenModelicaInterfaceFactory();
        factory.Adopt(new OpenModelicaInterface(_omc!));

        factory.Shutdown();

        Assert.True(child.WaitForExit(NeverThisLong),
            "the factory was shut down and what its omc started is still running");
    }

    /// <summary>
    /// A check still running as MLQT exits must not start an omc after the one session was ended -
    /// nothing would be left to end it.
    /// </summary>
    [Fact]
    public async Task AFactoryThatWasShutDown_StartsNoOmc()
    {
        var factory = new OpenModelicaInterfaceFactory();
        factory.UpdateSettings(new OpenModelicaSettings { OmcPath = WriteFakeOmc() });

        factory.Shutdown();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.GetOrCreateAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(_pidFile), "a factory that was shut down started omc");
    }

    /// <summary>
    /// A start given up on abandons the session outside the command lock, and the commonest reason it
    /// is given up on is Dispose itself, so the two meet. Each read the process field again after its
    /// own null check, and one clearing it between the other's check and its use threw a
    /// NullReferenceException out of Dispose - seen once on a loaded CI runner, where it replaced the
    /// outcome the caller was reporting. Raced many times because one meeting rarely lands in the gap.
    /// </summary>
    [Fact]
    public async Task AbandoningAndDisposingAtOnce_NeitherThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var round = 0; round < 100; round++)
        {
            using var sleeper = Process.Start(new ProcessStartInfo(
                OperatingSystem.IsWindows() ? "ping" : "sleep",
                OperatingSystem.IsWindows() ? "-n 60 127.0.0.1" : "60")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            }) ?? throw new InvalidOperationException("the stand-in omc did not start");
            using var handle = Process.GetProcessById(sleeper.Id);
            var session = new OpenModelicaInterface(sleeper);

            using var bothReady = new Barrier(2);
            var abandon = Task.Run(() => { bothReady.SignalAndWait(ct); session.Abandon(); }, ct);
            var dispose = Task.Run(() => { bothReady.SignalAndWait(ct); session.Dispose(); }, ct);

            await Task.WhenAll(abandon, dispose);
            Assert.True(handle.WaitForExit(NeverThisLong), $"round {round}: the stand-in omc was not ended");
        }
    }

    /// <summary>
    /// Each of the host's ways out can reach it, and an ordinary close reaches more than one; nor may
    /// disposing afterwards - which the TestHost's container does - throw over it.
    /// </summary>
    [Fact]
    public void ShuttingDownTwice_AndDisposingAfter_IsSafe()
    {
        var factory = new OpenModelicaInterfaceFactory();

        factory.Shutdown();
        factory.Shutdown();
        factory.Dispose();
        factory.Shutdown();

        Assert.False(factory.IsConnected);
    }
}

/// <summary>
/// B493 against a real omc: shutting the factory down, as MLQT does on exit, leaves no omc and nothing
/// omc started. On its own ports, so the shared fixture's session is not the one ended.
/// </summary>
[Trait("Requires", "OpenModelica")]
public sealed class LiveSessionEndTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"mlqt-omc-live-{Guid.NewGuid():N}");

    public LiveSessionEndTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    private static async Task<(OpenModelicaInterfaceFactory Factory, OpenModelicaInterface Session, Process Omc)> StartAsync()
    {
        var factory = new OpenModelicaInterfaceFactory();
        factory.UpdateSettings(new OpenModelicaSettings
        {
            OmcPath = OpenModelicaSettings.FindInstalledOmc(),
            StartupTimeoutMs = 30_000,
        });

        var session = (OpenModelicaInterface)await factory.GetOrCreateAsync(TestContext.Current.CancellationToken);
        var omc = Process.GetProcessById(session.ProcessId ?? throw new InvalidOperationException("no omc process"));
        return (factory, session, omc);
    }

    [Fact]
    public async Task ShuttingDown_EndsOmc()
    {
        var (factory, _, omc) = await StartAsync();
        using (omc)
        {
            factory.Shutdown();

            Assert.True(omc.WaitForExit(TimeSpan.FromSeconds(10)), "omc is still running after MLQT exited");
        }
    }

    /// <summary>
    /// The case the process tree is for: omc busy with a command that started a child, too busy to
    /// answer <c>quit()</c>. The child writes its own id, so the test watches the process that keeps
    /// running rather than a shell in front of it.
    /// </summary>
    [Fact]
    public async Task ShuttingDown_WhileOmcIsBusy_EndsWhatItStarted()
    {
        var pidFile = Path.Combine(_folder, "child.pid").Replace('\\', '/');
        var command = OperatingSystem.IsWindows()
            ? $"system(\"powershell -NoProfile -NonInteractive -Command \\\"[IO.File]::WriteAllText('{pidFile}', $PID); Start-Sleep -Seconds 120\\\"\")"
            : $"system(\"sh -c 'echo $$ > {pidFile}; exec sleep 120'\")";

        var (factory, session, omc) = await StartAsync();
        using (omc)
        {
            var busy = session.SendCommandAsync(command, cancellationToken: TestContext.Current.CancellationToken);

            var waited = Stopwatch.StartNew();
            while (!File.Exists(pidFile) || new FileInfo(pidFile).Length == 0)
            {
                Assert.True(waited.Elapsed < TimeSpan.FromSeconds(60), "omc never started the child");
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            using var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim()));
            try
            {
                factory.Shutdown();

                Assert.True(omc.WaitForExit(TimeSpan.FromSeconds(10)), "omc is still running after MLQT exited");
                Assert.True(child.WaitForExit(TimeSpan.FromSeconds(10)),
                    "what omc was running is still running after MLQT exited");
            }
            finally
            {
                try { if (!child.HasExited) child.Kill(); } catch { /* gone */ }
                try { await busy; } catch { /* ended underneath it, as intended */ }
            }
        }
    }
}
