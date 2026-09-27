using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace MLQT.Services.Tests;

/// <summary>
/// B411 — stopping a Dymola MLQT started through a launcher script ends Dymola, not only the script.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> On Linux detection prefers Dymola's launcher script (B395), and a
/// launcher that runs <c>bin64/dymola</c> as a child rather than <c>exec</c>-ing it leaves
/// <c>DymolaInterface</c> holding the shell. Stop and Dispose called <c>Kill()</c>, which ended the
/// shell and left Dymola running, still holding the port.</para>
///
/// <para><b>The fake launcher</b> is a <c>.cmd</c> on Windows and a <c>sh</c> script elsewhere. Each
/// starts one long-running child in the foreground, which writes its own process id to a file so the
/// test can watch it, and waits for it - the shape of a launcher that does not <c>exec</c>. The child is
/// Windows PowerShell on Windows, which every Windows machine has, and a subshell that <c>exec</c>s
/// <c>sleep</c> elsewhere, so the id it writes is the id of what keeps running. Nothing answers on the
/// port, so the start is cancelled once the child is up; the interface still owns what it launched.</para>
///
/// <para>Here rather than in DymolaInterface.Tests, which no CI job runs; this runs on both platforms.</para>
/// </remarks>
public sealed class DymolaLauncherStopTests : IDisposable
{
    private static readonly TimeSpan NeverThisLong = TimeSpan.FromSeconds(60);

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"mlqt-dymola-launcher-{Guid.NewGuid():N}");
    private readonly string _pidFile;
    private int? _childPid;

    public DymolaLauncherStopTests()
    {
        Directory.CreateDirectory(_folder);
        _pidFile = Path.Combine(_folder, "child.pid");
    }

    public void Dispose()
    {
        // A failing run must not leave a ten-minute sleeper behind.
        if (_childPid is { } pid)
            try { using var child = Process.GetProcessById(pid); child.Kill(); } catch { /* gone */ }
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    private string WriteLauncher()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_folder, "dymola-launcher.cmd");
            File.WriteAllText(path,
                "@echo off\r\n" +
                "powershell.exe -NoProfile -NonInteractive -Command " +
                $"\"[IO.File]::WriteAllText('{_pidFile}.tmp', $PID); " +
                $"Move-Item -Force '{_pidFile}.tmp' '{_pidFile}'; Start-Sleep -Seconds 600\"\r\n" +
                "echo launcher done\r\n");
            return path;
        }

        var script = Path.Combine(_folder, "dymola-launcher");
        File.WriteAllText(script,
            "#!/bin/sh\n" +
            $"sh -c 'echo $$ > \"{_pidFile}.tmp\"; mv \"{_pidFile}.tmp\" \"{_pidFile}\"; exec sleep 600'\n" +
            "echo launcher done\n");
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    private static int UnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Starts the launcher through the interface and returns once its child is running, with the
    /// interface owning the launcher.
    /// </summary>
    private async Task<(DymolaInterface.DymolaInterface Dymola, Process Child)> StartThroughLauncherAsync()
    {
        var dymola = new DymolaInterface.DymolaInterface(WriteLauncher(), UnusedPort(), "127.0.0.1",
            connectionWindow: TimeSpan.Zero);

        using var stop = new CancellationTokenSource();
        var start = dymola.StartDymolaProcessAsync(stop.Token);

        var waited = Stopwatch.StartNew();
        while (!File.Exists(_pidFile))
        {
            Assert.False(start.IsCompleted && !start.IsCanceled,
                $"the start ended before the launcher's child was up: {start.Exception?.GetBaseException().Message}");
            Assert.True(waited.Elapsed < NeverThisLong, "the launcher's child never started");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        _childPid = int.Parse(File.ReadAllText(_pidFile).Trim());
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);

        var child = Process.GetProcessById(_childPid.Value);
        Assert.False(child.HasExited);
        Assert.True(dymola.OwnsProcess, "the interface should own the launcher it started");
        return (dymola, child);
    }

    [Fact]
    public async Task Stopping_EndsWhatTheLauncherStarted()
    {
        var (dymola, child) = await StartThroughLauncherAsync();
        using (dymola)
        using (child)
        {
            await dymola.StopDymolaProcessAsync();

            Assert.False(dymola.OwnsProcess);
            Assert.True(child.WaitForExit(NeverThisLong),
                "the launcher was stopped and the program it started is still running");
        }
    }

    [Fact]
    public async Task Disposing_EndsWhatTheLauncherStarted()
    {
        var (dymola, child) = await StartThroughLauncherAsync();
        using (child)
        {
            dymola.Dispose();

            Assert.True(child.WaitForExit(NeverThisLong),
                "the interface was disposed and the program its launcher started is still running");
        }
    }
}
