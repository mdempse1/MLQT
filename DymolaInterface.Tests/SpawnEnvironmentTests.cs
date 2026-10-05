using System.Diagnostics;
using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for <see cref="DymolaInterface.SpawnEnvironmentVariables"/> and how Dymola is started -
/// built without launching a process. The Windows form is what most of these read; the Linux form,
/// which goes through <c>/bin/sh</c>, has tests of its own here and in <see cref="LinuxStartTests"/>.
/// </summary>
public class SpawnEnvironmentTests
{
    private static ProcessStartInfo CreateStartInfo(DymolaInterface dymola) => dymola.CreateStartInfo(windows: true);

    [Fact]
    public void SpawnEnvironmentVariables_DefaultsToNull()
    {
        using var dymola = new DymolaInterface("", 9999, "127.0.0.1");

        Assert.Null(dymola.SpawnEnvironmentVariables);
    }

    [Fact]
    public void CreateStartInfo_WithoutSpawnEnvironment_UsesInheritedEnvironmentOnly()
    {
        using var dymola = new DymolaInterface("C:/Dymola/bin64/Dymola.exe", 9999, "127.0.0.1");

        var startInfo = CreateStartInfo(dymola);

        Assert.Equal("C:/Dymola/bin64/Dymola.exe", startInfo.FileName);
        Assert.Equal("-serverport 9999", startInfo.Arguments);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public void CreateStartInfo_WithSpawnEnvironment_AddsVariables()
    {
        using var dymola = new DymolaInterface("C:/Dymola/bin64/Dymola.exe", 9999, "127.0.0.1")
        {
            SpawnEnvironmentVariables = new Dictionary<string, string>
            {
                ["TMP"] = @"C:\Users\test\AppData\Local\Temp",
                ["SALT_LICENSE_SERVER"] = "27000@licenses.example.com"
            }
        };

        var startInfo = CreateStartInfo(dymola);

        Assert.Equal(@"C:\Users\test\AppData\Local\Temp", startInfo.Environment["TMP"]);
        Assert.Equal("27000@licenses.example.com", startInfo.Environment["SALT_LICENSE_SERVER"]);
    }

    [Fact]
    public void CreateStartInfo_WithSpawnEnvironment_OverridesInheritedValue()
    {
        var inheritedName = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .First(k => !string.IsNullOrEmpty(k) && !k.StartsWith('='));
        using var dymola = new DymolaInterface("C:/Dymola/bin64/Dymola.exe", 9999, "127.0.0.1")
        {
            SpawnEnvironmentVariables = new Dictionary<string, string> { [inheritedName] = "overridden" }
        };

        var startInfo = CreateStartInfo(dymola);

        Assert.Equal("overridden", startInfo.Environment[inheritedName]);
    }

    /// <summary>
    /// On Linux Dymola is started through sh, which points its streams at /dev/null and execs it -
    /// with its path and arguments passed as arguments, never spliced into the script.
    /// </summary>
    [Fact]
    public void CreateStartInfo_OnLinux_GoesThroughShWithDymolaAsArguments()
    {
        using var dymola = new DymolaInterface("/opt/dymola 2026x/bin/dymola", 9999, "127.0.0.1", TimeSpan.Zero)
        {
            SpawnEnvironmentVariables = new Dictionary<string, string> { ["DYMOLA_X"] = "1" }
        };

        var startInfo = dymola.CreateStartInfo(windows: false);

        Assert.Equal("/bin/sh", startInfo.FileName);
        Assert.Equal(
            ["-c", DymolaInterface.NullStreamsThenExec, "/opt/dymola 2026x/bin/dymola", "-serverport", "9999"],
            startInfo.ArgumentList);
        Assert.Equal("1", startInfo.Environment["DYMOLA_X"]);
        Assert.False(startInfo.UseShellExecute);
    }

    /// <summary>
    /// On Windows nothing is redirected: <see cref="IsolatedProcess"/> starts Dymola with no handles at
    /// all, and a redirection here would only be ignored.
    /// </summary>
    [Fact]
    public void CreateStartInfo_OnWindows_RedirectsNothing()
    {
        using var dymola = new DymolaInterface("C:/Dymola/bin64/Dymola.exe", 9999, "127.0.0.1", TimeSpan.Zero);

        var startInfo = CreateStartInfo(dymola);

        Assert.False(startInfo.RedirectStandardInput || startInfo.RedirectStandardOutput || startInfo.RedirectStandardError);
    }
}
