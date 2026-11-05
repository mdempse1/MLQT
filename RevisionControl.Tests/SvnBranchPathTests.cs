namespace RevisionControl.Tests;

/// <summary>
/// <see cref="SvnRevisionControlSystem.BranchPathFor"/>: where a new SVN branch goes, given the name
/// typed, the repository's branch directories and which folders the repository has. No svn needed.
/// </summary>
public class SvnBranchPathTests
{
    private static readonly string[] Standard = ["trunk", "branches", "tags"];
    private static readonly string[] Custom = ["trunk", "tickets", "releases"];

    private static Func<string, bool> Folders(params string[] existing) =>
        folder => existing.Contains(folder, StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("test-7", "branches/test-7")]
    [InlineData("branches/test-7", "branches/test-7")]
    [InlineData("tags/v1", "tags/v1")]
    [InlineData("  /test-7/ ", "branches/test-7")]
    public void StandardLayout(string typed, string expected) =>
        Assert.Equal(expected, SvnRevisionControlSystem.BranchPathFor(typed, Standard, Folders("trunk", "branches", "tags")));

    [Fact]
    public void ANullList_IsTheStandardLayout() =>
        Assert.Equal("branches/x", SvnRevisionControlSystem.BranchPathFor("x", null, Folders()));

    [Theory]
    [InlineData("test-7", "tickets/test-7")]
    [InlineData("tickets/test-7", "tickets/test-7")]
    [InlineData("releases/1.0", "releases/1.0")]
    public void CustomLayout_ConfiguredContainersAreUsedAsTyped(string typed, string expected) =>
        Assert.Equal(expected, SvnRevisionControlSystem.BranchPathFor(typed, Custom, Folders()));

    /// <summary>
    /// The reported defect: the setting says tickets, the repository has branches, and the name
    /// typed was branches/test-7. It used to become tickets/branches/test-7.
    /// </summary>
    [Fact]
    public void AFolderTheRepositoryHas_IsUsedAsTyped_ThoughTheSettingNamesOthers() =>
        Assert.Equal("branches/test-7",
            SvnRevisionControlSystem.BranchPathFor("branches/test-7", Custom, Folders("trunk", "branches", "tags")));

    [Fact]
    public void ANestedFolderTheRepositoryHas_IsUsedAsTyped() =>
        Assert.Equal("branches/team/x",
            SvnRevisionControlSystem.BranchPathFor("branches/team/x", Standard, Folders("branches/team")));

    [Fact]
    public void AFolderTheRepositoryDoesNotHave_GoesInTheFirstContainer() =>
        Assert.Equal("branches/feature/x",
            SvnRevisionControlSystem.BranchPathFor("feature/x", Standard, Folders("trunk", "branches", "tags")));

    [Fact]
    public void ANameUnderTrunk_IsNeverCreatedInsideTrunk() =>
        Assert.Equal("branches/trunk/x",
            SvnRevisionControlSystem.BranchPathFor("trunk/x", Standard, Folders("trunk")));

    [Fact]
    public void ATrunkOnlyList_FallsBackToBranches() =>
        Assert.Equal("branches/x", SvnRevisionControlSystem.BranchPathFor("x", ["trunk"], Folders()));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/")]
    public void AnEmptyName_HasNoPath(string typed) =>
        Assert.Null(SvnRevisionControlSystem.BranchPathFor(typed, Standard, Folders()));

    // ---- What svn switch says it changed ------------------------------------------------------

    [Theory]
    [InlineData("Updating 'C:\\wc':\r\nAt revision 19.\r\n")]
    [InlineData("Updating 'C:\\wc':\nUpdated to revision 19.\n")]
    [InlineData("")]
    [InlineData(null)]
    public void ASwitchThatOnlyReportsTheRevision_ChangedNothing(string? output) =>
        Assert.False(SvnRevisionControlSystem.SwitchChangedFiles(output));

    [Theory]
    [InlineData("U    C:\\wc\\package.mo")]
    [InlineData("A    C:\\wc\\New.mo")]
    [InlineData("D    C:\\wc\\Old.mo")]
    [InlineData("G    C:\\wc\\Merged.mo")]
    [InlineData("C    C:\\wc\\Conflicted.mo")]
    [InlineData(" U   C:\\wc\\PropertyOnly.mo")]
    [InlineData("   C C:\\wc\\TreeConflict")]
    public void ASwitchWithAnItemLine_ChangedTheWorkingCopy(string itemLine) =>
        Assert.True(SvnRevisionControlSystem.SwitchChangedFiles(
            $"Updating 'C:\\wc':\r\n{itemLine}\r\nUpdated to revision 20.\r\n"));

    // ---- Paths on a command line --------------------------------------------------------------

    [Fact]
    public void Chunks_KeepEveryPathInOrder_AndStayInsideTheLimit()
    {
        var paths = Enumerable.Range(0, 50).Select(i => $"C:\\wc\\File{i:D2}.mo").ToList();

        var chunks = SvnRevisionControlSystem.CommandLineChunks(paths, maxChars: 100).ToList();

        Assert.Equal(paths, chunks.SelectMany(c => c));
        Assert.All(chunks, c => Assert.True(c.Sum(p => p.Length + 3) <= 100));
        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void APathLongerThanTheLimit_GoesInAChunkOfItsOwn()
    {
        var chunks = SvnRevisionControlSystem.CommandLineChunks(["short", new string('x', 200), "short2"], maxChars: 50).ToList();

        Assert.Equal(3, chunks.Count);
        Assert.Single(chunks[1]);
    }

    [Fact]
    public void NoPaths_NoChunks() =>
        Assert.Empty(SvnRevisionControlSystem.CommandLineChunks([]));

    [Fact]
    public void TheConflictSummary_IsNotAnItemLine() =>
        Assert.False(SvnRevisionControlSystem.SwitchChangedFiles(
            "Updated to revision 20.\nSummary of conflicts:\n  Text conflicts: 1\n"));
}
