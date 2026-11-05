namespace RevisionControl.Tests;

/// <summary>
/// <see cref="SvnRevisionControlSystem.CreateBranch(string, string, bool, IReadOnlyList{string}?)"/>
/// against a repository of this run's own: where the branch goes, and that a working copy with
/// uncommitted changes is switched to it with the changes intact and uncommitted.
/// </summary>
/// <remarks>
/// Named <c>SvnIntegration*</c> so the run without svn on PATH filters it out with the other classes
/// that need the client (<c>build/run-all-tests.ps1</c>).
/// </remarks>
public class SvnIntegrationCreateBranchTests : IClassFixture<SvnTestRepository>
{
    private readonly SvnTestRepository _repository;
    private readonly SvnRevisionControlSystem _svn = new();

    public SvnIntegrationCreateBranchTests(SvnTestRepository repository) => _repository = repository;

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private static string UrlOf(string workingCopy) =>
        SvnCli.Run("info", "--show-item", "url", workingCopy).EnsureSuccess("info").StdOut.Trim();

    private static string StatusOf(string workingCopy) =>
        SvnCli.Run("status", workingCopy).EnsureSuccess("status").StdOut;

    private bool Exists(string relative) => SvnCli.Run("info", _repository.RootUrl + "/" + relative).Success;

    [Fact]
    public void ABareName_GoesInTheFirstBranchDirectory()
    {
        var wc = _repository.CheckOut();
        var name = Unique("bare-");

        var result = _svn.CreateBranch(wc, name, switchToBranch: true, branchDirectories: null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(_repository.RootUrl + "/branches/" + name, UrlOf(wc));
    }

    [Fact]
    public void ANameWithTheBranchDirectory_IsNotGivenItTwice()
    {
        var wc = _repository.CheckOut();
        var name = Unique("prefixed-");

        var result = _svn.CreateBranch(wc, "branches/" + name, switchToBranch: true, branchDirectories: null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(_repository.RootUrl + "/branches/" + name, UrlOf(wc));
    }

    /// <summary>
    /// The defect this class was written for: the setting named folders the repository does not
    /// have, and <c>branches/x</c> became <c>tickets/branches/x</c>, which svn refused with E160013.
    /// </summary>
    [Fact]
    public void ANameUnderAnExistingFolder_IsUsedAsTyped_WhenTheSettingNamesOtherFolders()
    {
        var wc = _repository.CheckOut();
        var name = Unique("mismatch-");

        var result = _svn.CreateBranch(wc, "branches/" + name, switchToBranch: true, ["trunk", "tickets", "releases"]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(_repository.RootUrl + "/branches/" + name, UrlOf(wc));
        Assert.False(Exists("tickets"));
    }

    [Fact]
    public void AMissingBranchFolder_IsReportedWithWhatWasAttempted_AndNothingIsCreated()
    {
        var wc = _repository.CheckOut();
        var name = Unique("missing-");

        var result = _svn.CreateBranch(wc, "tickets/" + name, switchToBranch: true, ["trunk", "tickets", "releases"]);

        Assert.False(result.Success);
        Assert.Contains($"^/tickets/{name}", result.ErrorMessage);
        Assert.Contains("^/tickets does not exist", result.ErrorMessage);
        Assert.Contains("branches, tags, trunk", result.ErrorMessage);
        Assert.DoesNotContain("E160013", result.ErrorMessage);
        Assert.Equal(_repository.TrunkUrl, UrlOf(wc));
    }

    [Fact]
    public void AnExistingBranch_IsReportedWithItsPath()
    {
        var wc = _repository.CheckOut();

        var result = _svn.CreateBranch(wc, "feature-test", switchToBranch: false, branchDirectories: null);

        Assert.False(result.Success);
        Assert.Contains("^/branches/feature-test", result.ErrorMessage);
    }

    /// <summary>
    /// Changes made on trunk that belong on a branch: the branch is created, the working copy is
    /// switched to it, and every change - an edit, an added file - is still there and still
    /// uncommitted. Neither trunk nor the new branch has them.
    /// </summary>
    [Fact]
    public void AModifiedWorkingCopy_IsSwitchedWithItsChangesIntactAndUncommitted()
    {
        var wc = _repository.CheckOut();
        var readme = Path.Combine(wc, "README.txt");
        var edited = File.ReadAllText(readme) + "\nA local change\n";
        File.WriteAllText(readme, edited);
        var added = Path.Combine(wc, "Added.mo");
        File.WriteAllText(added, "model Added end Added;\n");
        SvnCli.Run("add", added).EnsureSuccess("add");
        var name = Unique("modified-");

        var result = _svn.CreateBranch(wc, name, switchToBranch: true, branchDirectories: null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(_repository.RootUrl + "/branches/" + name, UrlOf(wc));
        Assert.Equal(edited, File.ReadAllText(readme));
        var status = StatusOf(wc);
        Assert.Contains("README.txt", status);
        Assert.Contains("Added.mo", status);
        Assert.DoesNotContain("A local change",
            SvnCli.Run("cat", _repository.RootUrl + "/branches/" + name + "/README.txt").StdOut);
        Assert.False(SvnCli.Run("info", _repository.RootUrl + "/branches/" + name + "/Added.mo").Success);
    }

    [Fact]
    public void WithoutSwitching_TheWorkingCopyStaysWhereItWas()
    {
        var wc = _repository.CheckOut();
        var name = Unique("noswitch-");

        var result = _svn.CreateBranch(wc, name, switchToBranch: false, branchDirectories: null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(_repository.TrunkUrl, UrlOf(wc));
        Assert.True(Exists("branches/" + name));
    }
}
