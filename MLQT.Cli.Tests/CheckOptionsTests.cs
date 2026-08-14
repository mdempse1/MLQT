using MLQT.Cli;

namespace MLQT.Cli.Tests;

public class CheckOptionsTests
{
    [Fact]
    public void Defaults()
    {
        Assert.True(CheckOptions.TryParse(["lib"], out var o, out var err));
        Assert.Null(err);
        Assert.Equal("lib", o!.LibraryPath);
        Assert.Equal(OutputFormat.Console, o.Format);
        Assert.Equal(FailOnLevel.Error, o.FailOn);
        Assert.Null(o.ConfigPath);
        Assert.Null(o.OutPath);
    }

    [Fact]
    public void AllOptions()
    {
        Assert.True(CheckOptions.TryParse(
            ["lib", "--config", "c.json", "--format", "junit", "--out", "o.xml", "--fail-on", "warning", "--no-color"],
            out var o, out _));
        Assert.Equal("c.json", o!.ConfigPath);
        Assert.Equal(OutputFormat.JUnit, o.Format);
        Assert.Equal("o.xml", o.OutPath);
        Assert.Equal(FailOnLevel.Warning, o.FailOn);
        Assert.True(o.NoColor);
    }

    [Theory]
    [InlineData("--format", "xml")]
    [InlineData("--fail-on", "critical")]
    public void InvalidEnumValue_Fails(string flag, string value)
    {
        Assert.False(CheckOptions.TryParse(["lib", flag, value], out var o, out var err));
        Assert.Null(o);
        Assert.NotNull(err);
    }

    [Fact]
    public void MissingPath_Fails()
    {
        Assert.False(CheckOptions.TryParse(["--format", "json"], out _, out var err));
        Assert.Contains("library-path", err);
    }

    [Fact]
    public void UnknownOption_Fails()
        => Assert.False(CheckOptions.TryParse(["lib", "--nope"], out _, out _));

    [Fact]
    public void OptionMissingValue_Fails()
        => Assert.False(CheckOptions.TryParse(["lib", "--config"], out _, out _));

    [Theory]
    [InlineData("--baseline")]
    [InlineData("--changed-from")]
    public void ReservedOptions_FailWithNotSupported(string flag)
    {
        Assert.False(CheckOptions.TryParse(["lib", flag, "x"], out _, out var err));
        Assert.Contains("not supported", err);
    }
}
