using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// "Is this file inside that directory?" (B323) - a prefix is not an answer, a path segment is.
/// </summary>
public class PathContainmentTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-containment", "Lib");

    [Fact]
    public void AFileBeneathTheRootIsWithinIt()
    {
        Assert.True(PathContainment.IsWithin(Path.Combine(Root, "Sub", "A.mo"), Root));
        Assert.True(PathContainment.IsWithin(Path.Combine(Root, "A.mo"), Root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void TheRootIsWithinItself()
        => Assert.True(PathContainment.IsWithin(Root + Path.DirectorySeparatorChar, Root));

    [Fact]
    public void ASiblingThatSharesThePrefixIsNot()
    {
        Assert.False(PathContainment.IsWithin(Root + "Extra" + Path.DirectorySeparatorChar + "A.mo", Root));
        Assert.False(PathContainment.IsWithin(Root + "Extra", Root));
    }

    [Fact]
    public void AParentOrUnrelatedPathIsNot()
    {
        Assert.False(PathContainment.IsWithin(Path.GetDirectoryName(Root)!, Root));
        Assert.False(PathContainment.IsWithin(Path.Combine(Path.GetTempPath(), "elsewhere", "A.mo"), Root));
    }

    [Fact]
    public void RelativeSegmentsAreResolvedFirst()
        => Assert.False(PathContainment.IsWithin(Path.Combine(Root, "..", "Other", "A.mo"), Root));

    [Fact]
    public void AFilesystemRootContainsEverythingOnIt()
    {
        var fsRoot = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.True(PathContainment.IsWithin(Path.Combine(Root, "A.mo"), fsRoot));
    }

    [Fact]
    public void CaseIsIgnoredExceptOnLinux()
        => Assert.Equal(!OperatingSystem.IsLinux(), PathContainment.IsWithin(Path.Combine(Root.ToUpperInvariant(), "A.mo"), Root));

    [Theory]
    [InlineData("", "x")]
    [InlineData("x", "")]
    public void AnEmptyPathContainsAndIsContainedByNothing(string path, string root)
        => Assert.False(PathContainment.IsWithin(path, root));

    [Fact]
    public void APathThatCannotBeResolvedIsComparedAsWritten()
    {
        // A NUL is rejected by GetFullPath on every platform; the comparison still answers.
        Assert.False(PathContainment.IsWithin("bad\0path", Root));
    }
}
