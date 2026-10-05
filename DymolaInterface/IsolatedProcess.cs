using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DymolaInterface;

/// <summary>
/// Starts a Windows process that inherits <b>no handle at all</b> from this one.
/// </summary>
/// <remarks>
/// <para><c>Process.Start</c> with <c>UseShellExecute = false</c> calls <c>CreateProcess</c> with
/// <c>bInheritHandles</c> true, which hands the child <b>every inheritable handle this process
/// holds</b>, not only the three it passes as the child's standard streams. A host's own stdin and
/// stdout are inheritable - they were inherited - so redirecting the child's streams does not keep
/// the host's out of it. Measured with Dymola 2026x Refresh 1 under a host whose stdout was a pipe:
/// with the streams redirected the host exited and its reader still saw no end-of-file until Dymola
/// was ended. Under an MCP server over stdio that is the protocol channel, held open for as long as
/// the Dymola MLQT leaves running (B493).</para>
///
/// <para>.NET has no way to ask for less, so this calls <c>CreateProcess</c> itself, with
/// <c>bInheritHandles</c> false and no standard handles: Dymola is a GUI program spoken to over
/// HTTP and has no use for them. What it takes from the <see cref="ProcessStartInfo"/> is the file,
/// the arguments, the working directory and the environment - which is what makes
/// <c>SpawnEnvironmentVariables</c> still apply.</para>
/// </remarks>
internal static class IsolatedProcess
{
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;

    /// <summary>
    /// Starts <paramref name="startInfo"/>'s program with no inherited handles. Null when it ended
    /// before it could be looked up, as <c>Process.Start</c> can return null.
    /// </summary>
    /// <exception cref="Win32Exception">Windows would not start it - not found, not executable.</exception>
    [SupportedOSPlatform("windows")]
    public static Process? Start(ProcessStartInfo startInfo)
    {
        var commandLine = new StringBuilder(Quote(startInfo.FileName));
        if (!string.IsNullOrEmpty(startInfo.Arguments))
            commandLine.Append(' ').Append(startInfo.Arguments);

        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        var flags = CreateUnicodeEnvironment | (startInfo.CreateNoWindow ? CreateNoWindow : 0);
        var workingDirectory = string.IsNullOrEmpty(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory;

        if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, inheritHandles: false, flags,
                EnvironmentBlock(startInfo.Environment), workingDirectory, ref startup, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start {startInfo.FileName}");

        try
        {
            // The handle held until now is what keeps the id from being reused before this lookup.
            return Process.GetProcessById(info.ProcessId);
        }
        catch (ArgumentException)
        {
            return null;   // already ended
        }
        finally
        {
            CloseHandle(info.Thread);
            CloseHandle(info.Process);
        }
    }

    /// <summary>The program's path as the first word of a command line.</summary>
    internal static string Quote(string path) =>
        path.Length > 0 && !path.Contains(' ') && !path.Contains('\t') && !path.Contains('"') ? path : $"\"{path}\"";

    /// <summary>
    /// The environment as <c>CreateProcess</c> takes it: <c>name=value</c> entries, each ended by a
    /// null, sorted by name ignoring case as Windows expects, and a final null after the last.
    /// </summary>
    internal static string EnvironmentBlock(IDictionary<string, string?> environment)
    {
        var block = new StringBuilder();
        foreach (var pair in environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            block.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        return block.Append('\0').ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public IntPtr Reserved3, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessW")]
    private static extern bool CreateProcess(string? applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        string environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
