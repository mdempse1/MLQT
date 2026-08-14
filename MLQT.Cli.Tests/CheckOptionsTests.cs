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

    [Fact]
    public void ChangedFrom_Parses()
    {
        Assert.True(CheckOptions.TryParse(["lib", "--changed-from", "main"], out var o, out _));
        Assert.Equal("main", o!.ChangedFrom);
    }

    [Fact]
    public void Baseline_And_TouchedDebt_Parse()
    {
        Assert.True(CheckOptions.TryParse(
            ["lib", "--baseline", "bl.json", "--touched-debt", "fail"], out var o, out _));
        Assert.Equal("bl.json", o!.BaselinePath);
        Assert.Equal(TouchedDebtPolicy.Fail, o.TouchedDebt);
    }

    [Fact]
    public void TouchedDebt_Invalid_Fails()
        => Assert.False(CheckOptions.TryParse(["lib", "--touched-debt", "nope"], out _, out _));
}
