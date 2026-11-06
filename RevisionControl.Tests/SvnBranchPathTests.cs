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
        var paths = Enumerable.Range(0, 50)
            .Select(i => Path.Combine(Path.GetTempPath(), "wc", $"File{i:D2}.mo"))
            .ToList();

        // Room for three paths a chunk, whatever the temporary folder's path is on this machine.
        var limit = (paths[0].Length + 3) * 3;

        var chunks = SvnRevisionControlSystem.CommandLineChunks(paths, maxChars: limit).ToList();

        Assert.Equal(paths, chunks.SelectMany(c => c));
        Assert.All(chunks, c => Assert.True(c.Sum(p => p.Length + 3) <= limit));
        Assert.Equal(17, chunks.Count);
    }

    [Fact]
    public void APathLongerThanTheLimit_GoesInAChunkOfItsOwn()
    {
        var chunks = SvnRevisionControlSystem.CommandLineChunks(["short", new string('x', 200), "short2"], maxChars: 50).ToList();

        Assert.Equal(3, chunks.Count);
        Assert.Single(chunks[1]);
    }

    [Fact]
    public void TheFoldersBetween_ARootAndAPath_NearestTheRootFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), "mlqt-wc");
        var path = Path.Combine(root, "A", "B", "C.mo");

        Assert.Equal([Path.Combine(root, "A"), Path.Combine(root, "A", "B")],
            SvnRevisionControlSystem.FoldersBetween(root, path));
    }

    [Fact]
    public void AFileAtTheRoot_HasNoFoldersBetween() =>
        Assert.Empty(SvnRevisionControlSystem.FoldersBetween(
            Path.Combine(Path.GetTempPath(), "mlqt-wc"), Path.Combine(Path.GetTempPath(), "mlqt-wc", "A.mo")));

    [Fact]
    public void ASiblingFolderThatSharesThePrefix_IsNotInside()
    {
        // "wc-other" starts with "wc" but is not inside it.
        var root = Path.Combine(Path.GetTempPath(), "mlqt-wc");
        Assert.Empty(SvnRevisionControlSystem.FoldersBetween(root, Path.Combine(Path.GetTempPath(), "mlqt-wc-other", "A", "B.mo")));
    }

    [Fact]
    public void NoPaths_NoChunks() =>
        Assert.Empty(SvnRevisionControlSystem.CommandLineChunks([]));

    [Fact]
    public void AFileTheSwitchRestored_ChangedTheWorkingCopy() =>
        Assert.True(SvnRevisionControlSystem.SwitchChangedFiles(
            "Updating 'C:\\wc':\r\nRestored 'C:\\wc\\Missing.mo'\r\nAt revision 20.\r\n"));

    // ---- Paths status could not place --------------------------------------------------------

    private static readonly string Wc = Path.Combine(Path.GetTempPath(), "mlqt-wc");
    private static readonly string Versioned = Path.Combine(Wc, "Versioned.mo");
    private static readonly string New = Path.Combine(Wc, "NewFolder", "New.mo");
    private static readonly string Gone = Path.Combine(Wc, "Gone.mo");

    private static Dictionary<string, SvnRevisionControlSystem.PathKind> Classify(System.Xml.Linq.XDocument? info) =>
        SvnRevisionControlSystem.ClassifyUnplaced([Versioned, New, Gone], info, p => p != Gone)
            .ToDictionary(k => k.Path, k => k.Kind);

    [Fact]
    public void WhatSvnInfoLists_IsVersioned_TheRestOnDiskUnversioned()
    {
        var info = System.Xml.Linq.XDocument.Parse($"<info><entry path=\"{Versioned}\" /></info>");

        var kinds = Classify(info);

        Assert.Equal(SvnRevisionControlSystem.PathKind.Versioned, kinds[Versioned]);
        Assert.Equal(SvnRevisionControlSystem.PathKind.Unversioned, kinds[New]);
        Assert.Equal(SvnRevisionControlSystem.PathKind.NotFound, kinds[Gone]);
    }

    /// <summary>
    /// svn info failed as well as status, so nothing is known. Taken for unversioned, a revert
    /// deleted the user's modified files.
    /// </summary>
    [Fact]
    public void WhenSvnInfoGaveNoAnswer_NothingIsTakenForUnversioned() =>
        Assert.All(Classify(info: null).Values, kind => Assert.Equal(SvnRevisionControlSystem.PathKind.Versioned, kind));

    [Fact]
    public void TheConflictSummary_IsNotAnItemLine() =>
        Assert.False(SvnRevisionControlSystem.SwitchChangedFiles(
            "Updated to revision 20.\nSummary of conflicts:\n  Text conflicts: 1\n"));
}
