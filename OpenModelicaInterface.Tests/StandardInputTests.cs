using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// omc gets a stdin of its own, never the host's. Needs no omc; the live half, which reproduces what
/// inheriting it did, is <see cref="HostReadingStdinTests"/>.
/// </summary>
public class StandardInputTests
{
    [Fact]
    public void Omc_IsStartedWithEveryStandardStreamItsOwn()
    {
        var startInfo = OpenModelicaInterface.CreateStartInfo("omc", "mlqt-x", OpenModelicaInterface.AnyPort);

        Assert.True(startInfo.RedirectStandardInput, "omc would share the host's stdin - an MCP server's protocol channel");
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(OpenModelicaInterface.StartArguments("mlqt-x", OpenModelicaInterface.AnyPort), startInfo.Arguments);
    }
}

/// <summary>Tests that replace the test host's own stdin, and so run with nothing beside them.</summary>
[CollectionDefinition(nameof(HostStdinCollection), DisableParallelization = true)]
public class HostStdinCollection;

/// <summary>
/// A host blocked reading its stdin - an MCP server over stdio, which is what a simulation adapter
/// built on this library may run in - can still start omc.
/// </summary>
/// <remarks>
/// <para>Inheriting the host's stdin, omc did not start within 30s while the host had a read of it
/// pending - most likely because synchronous I/O on one Windows pipe is serialised, so omc's start-up
/// waited behind the read. Reported by a project that builds on this library.</para>
///
/// <para>The test makes its own process that host: it points this process's stdin at a pipe of its
/// own, which <c>Process.Start</c> reads as the handle a child inherits, and blocks a thread reading
/// it. Windows only, since that is where it was seen and what <c>SetStdHandle</c> is; the
/// redirection itself is held on every platform by <see cref="StandardInputTests"/>. Seen to fail
/// with <c>RedirectStandardInput</c> taken out of the start.</para>
///
/// <para>Run alone: the stdin it replaces is the whole test host's, so a process another test started
/// meanwhile without redirecting its own - the fake omc of <c>SessionEndTests</c> - would be handed
/// the blocked pipe and could hang behind it exactly as omc did.</para>
/// </remarks>
[Trait("Requires", "OpenModelica")]
[Collection(nameof(HostStdinCollection))]
public class HostReadingStdinTests
{
    private const int StdInputHandle = -10;

    [Fact]
    public async Task Omc_StartsWhileTheHostIsReadingItsStdin()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await StartWhileReadingStdinAsync();
    }

    [SupportedOSPlatform("windows")]
    private static async Task StartWhileReadingStdinAsync()
    {
        // Inheritable, as the stdin a host is given by whatever started it is.
        var inheritable = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        Assert.True(CreatePipe(out var readEnd, out var writeEnd, ref inheritable, 0), "no pipe");
        var original = GetStdHandle(StdInputHandle);
        Thread? reader = null;
        try
        {
            Assert.True(SetStdHandle(StdInputHandle, readEnd.DangerousGetHandle()), "stdin not replaced");

            // The host's pending read: nothing is written until the start is over.
            reader = new Thread(() => ReadFile(readEnd, new byte[1], 1, out _, IntPtr.Zero)) { IsBackground = true };
            reader.Start();
            await Task.Delay(500, TestContext.Current.CancellationToken);

            using var omc = new OpenModelicaInterface(OpenModelicaSettings.FindInstalledOmc())
            {
                StartupTimeout = TimeSpan.FromSeconds(20)
            };
            await omc.StartAsync(TestContext.Current.CancellationToken);

            Assert.False(string.IsNullOrEmpty(await omc.GetVersionAsync()));
        }
        finally
        {
            SetStdHandle(StdInputHandle, original);
            WriteFile(writeEnd, [0], 1, out _, IntPtr.Zero);   // lets the read finish
            reader?.Join(TimeSpan.FromSeconds(10));
            writeEnd.Dispose();
            readEnd.Dispose();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, ref SecurityAttributes attributes, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public bool InheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int handle, IntPtr value);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int count, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int count, out int written, IntPtr overlapped);
}
