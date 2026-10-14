using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// Enum values MLQT.Services writes as text are spelled, not taken from <c>ToString()</c>, which an
/// obfuscated build renames (B502): a baseline's drift warning, and the VCS a settings store records.
/// Each spelling is the member's own name, so writing them down changed no output.
/// </summary>
public class SpelledEnumNameTests
{
    [Fact]
    public void ASeverityIsNamedAsItsMember()
    {
        Assert.All(Enum.GetValues<RuleSeverity>(), s => Assert.Equal(s.ToString(), Baseline.NameOf(s)));
    }

    [Fact]
    public void AVcsIsNamedAsItsMember()
    {
        Assert.All(Enum.GetValues<RepositoryVcsType>(), v => Assert.Equal(v.ToString(), RepositoryService.VcsTypeName(v)));
    }
}
