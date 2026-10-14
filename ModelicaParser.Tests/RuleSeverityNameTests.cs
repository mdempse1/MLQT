using System.Text.Json;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// <see cref="RuleSeverity"/>'s names are in every committed <c>.mlqt/settings.json</c> and baseline,
/// so they are written by an explicit converter rather than read from the enum's members: an
/// obfuscated build can rename those members or delete them from metadata altogether (B502). These
/// hold the converter to what the reflection-based string-enum converter did before B502 - case
/// ignored, whitespace trimmed, numbers and number strings accepted - so no file reads differently.
/// </summary>
public class RuleSeverityNameTests
{
    [Theory]
    [InlineData(RuleSeverity.Off, "\"Off\"")]
    [InlineData(RuleSeverity.Info, "\"Info\"")]
    [InlineData(RuleSeverity.Warning, "\"Warning\"")]
    [InlineData(RuleSeverity.Error, "\"Error\"")]
    public void IsWrittenAndReadByItsName(RuleSeverity severity, string json)
    {
        Assert.Equal(json, JsonSerializer.Serialize(severity));
        Assert.Equal(severity, JsonSerializer.Deserialize<RuleSeverity>(json));
    }

    [Fact]
    public void EveryMemberIsNamedAsItself()
    {
        // The names are spelled out, so a member added without its spelling would be written as a
        // number. Enum.GetValues is fine here: this runs on the plain build.
        Assert.All(Enum.GetValues<RuleSeverity>(), s => Assert.Equal(s.ToString(), RuleSeverityText.Of(s)));
    }

    [Theory]
    [InlineData("\"warning\"", RuleSeverity.Warning)]
    [InlineData("\"ERROR\"", RuleSeverity.Error)]
    [InlineData("2", RuleSeverity.Warning)]
    [InlineData("\"3\"", RuleSeverity.Error)]
    [InlineData("7", (RuleSeverity)7)]
    [InlineData("\" Warning \"", RuleSeverity.Warning)]
    [InlineData("\"Info, Warning\"", RuleSeverity.Error)] // the original read a list as flags: 1 | 2
    public void ReadsWhatTheReflectionConverterRead(string json, RuleSeverity expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<RuleSeverity>(json));
    }

    [Theory]
    [InlineData("\"Bogus\"")]
    [InlineData("\"\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void RefusesWhatTheReflectionConverterRefused(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RuleSeverity>(json));
    }

    [Theory]
    [InlineData("\"Warning,\"", RuleSeverity.Warning)]
    [InlineData("\"Info,Warning,\"", RuleSeverity.Error)]
    public void ToleratesOneTrailingCommaAsTheReflectionConverterDid(string json, RuleSeverity expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<RuleSeverity>(json));
    }

    [Theory]
    [InlineData("\",Warning\"")]
    [InlineData("\",\"")]
    [InlineData("\"Warning,,Error\"")]
    [InlineData("\"Warning, x\"")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void RefusesEveryOtherShapeTheReflectionConverterRefused(string json)
    {
        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RuleSeverity>(json));
        Assert.Contains("rule severity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsADictionaryKeyByItsName()
    {
        // The reflection converter supported this, and RuleSeverity is public in an assembly others
        // build on, so its converter has to keep doing so.
        var json = JsonSerializer.Serialize(new Dictionary<RuleSeverity, int> { [RuleSeverity.Warning] = 1 });

        Assert.Equal("""{"Warning":1}""", json);
        Assert.Equal(RuleSeverity.Warning, JsonSerializer.Deserialize<Dictionary<RuleSeverity, int>>("""{"warning":1}""")!.Keys.Single());
        Assert.Equal(RuleSeverity.Warning, JsonSerializer.Deserialize<Dictionary<RuleSeverity, int>>("""{"2":1}""")!.Keys.Single());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<RuleSeverity, int>>("""{"Bogus":1}"""));
    }

    [Fact]
    public void WritesAnUnnamedValueAsItsNumber()
    {
        Assert.Equal("7", JsonSerializer.Serialize((RuleSeverity)7));
    }

    [Fact]
    public void KeepsItsNameInsideACollection()
    {
        var map = new Dictionary<string, RuleSeverity> { ["MLQT.Doc.ClassDescription"] = RuleSeverity.Error };

        var json = JsonSerializer.Serialize(map);

        Assert.Equal("""{"MLQT.Doc.ClassDescription":"Error"}""", json);
        Assert.Equal(RuleSeverity.Error, JsonSerializer.Deserialize<Dictionary<string, RuleSeverity>>(json)!["MLQT.Doc.ClassDescription"]);
    }

    [Theory]
    [InlineData("Off", RuleSeverity.Off)]
    [InlineData("warning", RuleSeverity.Warning)]
    public void ParsesANameIgnoringCase(string text, RuleSeverity expected)
    {
        Assert.True(RuleSeverityText.TryParse(text, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void ParsesNothingElse()
    {
        Assert.False(RuleSeverityText.TryParse("Bogus", out _));
        Assert.False(RuleSeverityText.TryParse(null, out _));
    }
}
