using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// <see cref="RuleSeverity"/>'s names are in every committed <c>.mlqt/settings.json</c> and baseline,
/// so each is written down rather than taken from the member, which an obfuscated build renames
/// (B502). These hold the written names to the members' own, so writing them down moved nothing.
/// </summary>
public class RuleSeverityNameTests
{
    [Fact]
    public void EverySeverityNamesItselfOnDisk()
    {
        var fields = typeof(RuleSeverity).GetFields(BindingFlags.Public | BindingFlags.Static);

        Assert.Equal(4, fields.Length);
        Assert.All(fields, field =>
            Assert.Equal(field.Name, field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name));
    }

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
}
