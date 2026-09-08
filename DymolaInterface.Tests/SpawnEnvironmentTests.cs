using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for <see cref="DymolaInterface.SpawnEnvironmentVariables"/>. The start info is
/// built by a private method; reflection is used to inspect it without launching a
/// process, following the same approach as <see cref="Fakes.DymolaTestHarness"/>.
/// </summary>
public class SpawnEnvironmentTests
{
    private static ProcessStartInfo CreateStartInfo(DymolaInterface dymola)
    {
        var method = typeof(DymolaInterface).GetMethod("CreateStartInfo",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CreateStartInfo method not found");
        return (ProcessStartInfo)method.Invoke(dymola, null)!;
    }

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
}
