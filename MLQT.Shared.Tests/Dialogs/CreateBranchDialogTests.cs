using MLQT.Shared.Dialogs;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// <see cref="CreateBranchDialog.IsValidBranchName"/> — the subset of git's ref-name rules the
/// dialog applies while the user is still typing.
///
/// <para>Both directions cost something. Rejecting a valid name is visible and infuriating;
/// accepting an invalid one means git refuses it later with a message about ref formats that says
/// nothing about which character was the problem.</para>
/// </summary>
public class CreateBranchDialogTests
{
    [Theory]
    [InlineData("feature")]
    [InlineData("feature/add-thing")]
    [InlineData("release/2.1")]
    [InlineData("fix-123")]
    [InlineData("user.name/topic")]
    [InlineData("MLQT-42")]
    [InlineData("v1.2.3")]
    public void OrdinaryBranchNames_AreAccepted(string name)
    {
        Assert.True(CreateBranchDialog.IsValidBranchName(name), name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyName_IsRejected(string? name)
    {
        Assert.False(CreateBranchDialog.IsValidBranchName(name!));
    }

    [Theory]
    [InlineData("-feature")]   // git reads a leading dash as an option
    [InlineData(".hidden")]
    public void ALeadingDashOrDot_IsRejected(string name)
    {
        Assert.False(CreateBranchDialog.IsValidBranchName(name), name);
    }

    [Theory]
    [InlineData("feature..thing")]  // .. is git's range syntax
    [InlineData("feature~1")]       // ~ and ^ are revision navigation
    [InlineData("feature^2")]
    [InlineData("feature:thing")]   // : separates refspecs
    [InlineData("feature?")]
    [InlineData("feature*")]
    [InlineData("feature[1]")]
    [InlineData(@"feature\thing")]
    [InlineData("my feature")]
    public void CharactersGitReservesOrRefuses_AreRejected(string name)
    {
        Assert.False(CreateBranchDialog.IsValidBranchName(name), name);
    }

    [Theory]
    [InlineData("feature.lock")]  // collides with git's own lock files
    [InlineData("feature/")]
    [InlineData("feature.")]
    public void NamesEndingBadly_AreRejected(string name)
    {
        Assert.False(CreateBranchDialog.IsValidBranchName(name), name);
    }

    [Fact]
    public void ADotInsideTheName_IsFine()
    {
        // Only a leading dot, a trailing dot and a doubled dot are problems — version-numbered
        // branches like release/2.1 are ordinary and must not be refused.
        Assert.True(CreateBranchDialog.IsValidBranchName("release/2.1"));
    }

    [Fact]
    public void ANameEndingInLockButNotDotLock_IsFine()
    {
        // ".lock" is the suffix git reserves; "lock" on its own is just a word.
        Assert.True(CreateBranchDialog.IsValidBranchName("feature/deadlock"));
    }
}
