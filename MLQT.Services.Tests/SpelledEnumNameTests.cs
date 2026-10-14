using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// Enum values MLQT.Services writes as text are spelled, not taken from <c>ToString()</c>, which an
/// obfuscated build renames (B502): here, the VCS a settings store records. A severity's spelling,
/// which a baseline's drift warning uses, is held by ModelicaParser's RuleSeverityNameTests.
/// Each spelling is the member's own name, so writing them down changed no output.
/// </summary>
public class SpelledEnumNameTests
{
    [Fact]
    public void AVcsIsNamedAsItsMember()
    {
        Assert.All(Enum.GetValues<RepositoryVcsType>(), v => Assert.Equal(v.ToString(), RepositoryService.VcsTypeName(v)));
    }
}
