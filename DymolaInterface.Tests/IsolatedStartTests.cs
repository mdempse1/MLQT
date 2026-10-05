using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DymolaInterface.Tests;

/// <summary>
/// Not run beside anything else: these hold an inheritable handle open, and a process another test
/// started meanwhile would inherit it and read as the leak they look for.
/// </summary>
[CollectionDefinition("Inheritable handles", DisableParallelization = true)]
public class InheritableHandlesCollection;

/// <summary>
/// A Dymola started on Windows inherits no handle of the host's. Needs no Dymola: any long-running
/// program shows what a child is given.
/// </summary>
/// <remarks>
/// Measured with Dymola 2026x Refresh 1: started by <c>Process.Start</c>, it held its host's stdout -
/// an MCP server's protocol channel - open after the host had exited, until Dymola itself was ended,
/// redirected streams or not. Seen to fail with <see cref="IsolatedProcess"/> replaced by
/// <c>Process.Start</c>.
/// </remarks>
[Collection("Inheritable handles")]
public sealed class IsolatedProcessTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mlqt-isolated-{Guid.NewGuid():N}");
    private Process? _child;

    public IsolatedProcessTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { if (_child is { HasExited: false }) _child.Kill(entireProcessTree: true); } catch { /* gone */ }
        _child?.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void AStartedProcess_DoesNotKeepTheHostsPipeOpen()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.True(TheHostsPipeClosesWithAChildRunning(), "the child inherited the host's pipe and holds it open");
    }

    [SupportedOSPlatform("windows")]
    private bool TheHostsPipeClosesWithAChildRunning()
    {
        // A pipe of the host's, inheritable as the stdout a host is given by whatever started it is.
        var inheritable = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        Assert.True(CreatePipe(out var readEnd, out var writeEnd, ref inheritable, 0), "no pipe");
        using (readEnd)
        {
            using (writeEnd)
            {
                _child = IsolatedProcess.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "ping.exe"),
                    Arguments = "-n 30 127.0.0.1",
                    CreateNoWindow = true
                });
                Assert.NotNull(_child);
                Assert.False(_child.HasExited);
            }

            // The host's write end is closed. If nobody else holds it, the read sees the end of the pipe.
            var read = Task.Run(() => ReadFile(readEnd, new byte[1], 1, out _, IntPtr.Zero));
            return read.Wait(TimeSpan.FromSeconds(10)) && !read.Result;
        }
    }

    /// <summary>The environment the start info carries is the one the program sees -
    /// <c>SpawnEnvironmentVariables</c> depends on it.</summary>
    [Fact]
    public void AStartedProcess_GetsTheStartInfosEnvironment()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var output = Path.Combine(_folder, "env.txt");
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c echo %MLQT_ISOLATED_TEST%> \"{output}\"",
            CreateNoWindow = true
        };
        startInfo.Environment["MLQT_ISOLATED_TEST"] = "from the start info";

        _child = Start(startInfo);
        Assert.NotNull(_child);
        Assert.True(_child.WaitForExit(TimeSpan.FromSeconds(30)), "cmd did not finish");

        Assert.Equal("from the start info", File.ReadAllText(output).Trim());
    }

    [Fact]
    public void AProgramThatDoesNotExist_IsReportedAsWindowsWould()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var startInfo = new ProcessStartInfo { FileName = Path.Combine(_folder, "no such Dymola.exe") };

        Assert.Throws<System.ComponentModel.Win32Exception>(() => Start(startInfo));
    }

    [Fact]
    public void Quote_QuotesOnlyAPathThatNeedsIt()
    {
        Assert.Equal(@"C:\Dymola\bin64\Dymola.exe", IsolatedProcess.Quote(@"C:\Dymola\bin64\Dymola.exe"));
        Assert.Equal("\"C:\\Program Files\\Dymola 2026x\\bin64\\Dymola.exe\"",
            IsolatedProcess.Quote(@"C:\Program Files\Dymola 2026x\bin64\Dymola.exe"));
        Assert.Equal("\"\"", IsolatedProcess.Quote(""));
    }

    [Fact]
    public void EnvironmentBlock_IsSortedNullSeparatedAndEndedTwice()
    {
        var block = IsolatedProcess.EnvironmentBlock(new Dictionary<string, string?>
        {
            ["b"] = "2",
            ["A"] = "1",
            ["c"] = null,
        });

        Assert.Equal("A=1\0b=2\0c=\0\0", block);
    }

    private static Process? Start(ProcessStartInfo startInfo) =>
        OperatingSystem.IsWindows() ? IsolatedProcess.Start(startInfo) : throw new PlatformNotSupportedException();

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public bool InheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, ref SecurityAttributes attributes, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int count, out int read, IntPtr overlapped);
}

/// <summary>
/// On Linux, Dymola's three standard streams are <c>/dev/null</c> and it is the process MLQT
/// started, not a shell around it. A fake Dymola reports what it was given; needs no Dymola.
/// </summary>
[Collection("Inheritable handles")]
public sealed class LinuxStartTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mlqt-linux-start-{Guid.NewGuid():N}");

    public LinuxStartTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void Dymola_GetsNullStreamsItsArgumentsAndTheProcessMlqtStarted()
    {
        if (OperatingSystem.IsWindows())
            return;

        // A space in the path: it reaches the shell as an argument, so it needs no quoting.
        var report = Path.Combine(_folder, "report");
        var fake = Path.Combine(_folder, "fake dymola");
        File.WriteAllText(fake,
            "#!/bin/sh\n" +
            // Read before anything is redirected: dash points the shell's own stdout at a command's
            // redirection while that command runs, so a report written meanwhile would name itself.
            "fds=$(readlink /proc/$$/fd/0 /proc/$$/fd/1 /proc/$$/fd/2)\n" +
            $"printf '%s\\n' \"$fds\" \"$$\" \"$*\" > \"{report}.tmp\"\n" +
            $"mv \"{report}.tmp\" \"{report}\"\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var dymola = new DymolaInterface(fake, 9999, "127.0.0.1", TimeSpan.Zero);
        using var process = Process.Start(dymola.CreateStartInfo(windows: false))!;
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "the fake Dymola did not finish");

        var lines = File.ReadAllLines(report);
        Assert.Equal(["/dev/null", "/dev/null", "/dev/null"], lines[..3]);
        Assert.Equal(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), lines[3]);
        Assert.Equal("-serverport 9999", lines[4]);
    }
}
