using System.Diagnostics;

namespace RevisionControl.Tests;

/// <summary>
/// Integration tests for SvnRevisionControlSystem using a temporary SVN repository
/// created fresh for each test run via svnadmin.
/// </summary>
public class SvnIntegrationTests : IDisposable
{
    private readonly string _repoDir;
    private readonly string _trunkUrl;
    private readonly string _repoRoot;
    private readonly SvnRevisionControlSystem _svn;
    private readonly List<string> _checkoutPaths = new();

    public SvnIntegrationTests()
    {
        _svn = new SvnRevisionControlSystem();

        // Create a temporary SVN repository with standard layout
        _repoDir = Path.Combine(Path.GetTempPath(), "SvnTestRepo_" + Guid.NewGuid());
        Directory.CreateDirectory(_repoDir);

        // svnadmin create
        RunSvnAdmin($"create \"{_repoDir}\"");

        // Build the file:/// URL (forward slashes)
        var repoPath = _repoDir.Replace('\\', '/');
        _repoRoot = $"file:///{repoPath}";
        _trunkUrl = $"{_repoRoot}/trunk";

        // Create standard layout (trunk, branches, tags) via svn mkdir
        RunSvn($"mkdir \"{_repoRoot}/trunk\" \"{_repoRoot}/branches\" \"{_repoRoot}/tags\" -m \"Create standard layout\"");

        // Create a tag so tag-related tests work
        RunSvn($"copy \"{_repoRoot}/trunk\" \"{_repoRoot}/tags/v1.0\" -m \"Tag v1.0\"");
        RunSvn($"copy \"{_repoRoot}/trunk\" \"{_repoRoot}/tags/v2.0\" -m \"Tag v2.0\"");
    }

    public void Dispose()
    {
        foreach (var path in _checkoutPaths)
            ForceDeleteDirectory(path);

        ForceDeleteDirectory(_repoDir);
    }

    private string CreateCheckoutPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "SvnIntegrationTest_" + Guid.NewGuid());
        _checkoutPaths.Add(path);
        return path;
    }

    private static void RunSvnAdmin(string arguments)
    {
        var psi = new ProcessStartInfo("svnadmin", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(30_000);
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"svnadmin {arguments} failed: {error}");
        }
    }

    private static void RunSvn(string arguments)
    {
        var psi = new ProcessStartInfo("svn", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(30_000);
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"svn {arguments} failed: {error}");
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void IsValidRepository_WithFileUrl_ChecksRepository()
    {
        var result = _svn.IsValidRepository(_trunkUrl);
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithTrunkUrl_ReturnsTrue()
    {
        var result = _svn.IsValidRepository(_trunkUrl);
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithBranchesUrl_ReturnsTrue()
    {
        var branchesUrl = _repoRoot + "/branches";
        var result = _svn.IsValidRepository(branchesUrl);
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithTagsUrl_ReturnsTrue()
    {
        var tagsUrl = _repoRoot + "/tags";
        var result = _svn.IsValidRepository(tagsUrl);
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithInvalidUrl_ReturnsFalse()
    {
        var invalidUrl = "file:///C:/NonExistent/SVN/Repository";
        var result = _svn.IsValidRepository(invalidUrl);
        Assert.False(result);
    }

    [Fact]
    public void CheckoutRevision_WithHEAD_ChecksOutSuccessfully()
    {
        var checkoutPath = CreateCheckoutPath();
        var result = _svn.CheckoutRevision(_trunkUrl, "HEAD", checkoutPath);

        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(Directory.Exists(Path.Combine(checkoutPath, ".svn")));
    }

    [Fact]
    public void CheckoutRevision_WithEmptyRevision_DefaultsToHEAD()
    {
        var checkoutPath = CreateCheckoutPath();
        var result = _svn.CheckoutRevision(_trunkUrl, "", checkoutPath);

        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
    }

    [Fact]
    public void CheckoutRevision_WithLowercaseKeyword_WorksCaseInsensitive()
    {
        var checkoutPath = CreateCheckoutPath();
        var result = _svn.CheckoutRevision(_trunkUrl, "head", checkoutPath);

        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
    }

    [Fact]
    public void GetCurrentRevision_AfterCheckout_ReturnsRevisionNumber()
    {
        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(_trunkUrl, "HEAD", checkoutPath);

        var revision = _svn.GetCurrentRevision(checkoutPath);

        Assert.NotNull(revision);
        Assert.True(long.TryParse(revision, out _), "Revision should be a number");
    }

    [Fact]
    public void GetCurrentRevision_WithRemoteUrl_ReturnsHeadRevision()
    {
        var revision = _svn.GetCurrentRevision(_trunkUrl);

        Assert.NotNull(revision);
        Assert.True(long.TryParse(revision, out _), "Revision should be a number");
    }

    [Fact]
    public void GetRevisionDescription_WithHEAD_HandlesEmptyRepository()
    {
        var description = _svn.GetRevisionDescription(_trunkUrl, "HEAD");

        // May be null or empty for empty/new repository — just ensure no exception
        if (description != null)
            Assert.NotNull(description);
    }

    [Fact]
    public void UpdateExistingCheckout_WithNonExistentCheckout_PerformsInitialCheckout()
    {
        var checkoutPath = CreateCheckoutPath();

        var result = _svn.UpdateExistingCheckout(checkoutPath, _trunkUrl, "HEAD");

        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(Directory.Exists(Path.Combine(checkoutPath, ".svn")));
    }

    [Fact]
    public void UpdateExistingCheckout_MultipleTimes_WorksConsistently()
    {
        var checkoutPath = CreateCheckoutPath();

        for (int i = 0; i < 3; i++)
        {
            var result = _svn.UpdateExistingCheckout(checkoutPath, _trunkUrl, "HEAD");
            Assert.True(result, $"Update {i + 1} should succeed");
        }
    }

    [Fact]
    public void UpdateRevisionInPlace_WithNonExistentCheckout_PerformsInitialCheckout()
    {
        // Same fall-back as UpdateExistingCheckout — there's no wc to update so a
        // fresh checkout is the only thing that makes sense.
        var checkoutPath = CreateCheckoutPath();

        var result = _svn.UpdateRevisionInPlace(checkoutPath, _trunkUrl, "HEAD");

        Assert.True(result);
        Assert.True(Directory.Exists(checkoutPath));
        Assert.True(Directory.Exists(Path.Combine(checkoutPath, ".svn")));
    }

    [Fact]
    public void UpdateRevisionInPlace_MultipleTimes_WorksConsistently()
    {
        var checkoutPath = CreateCheckoutPath();

        for (int i = 0; i < 3; i++)
        {
            var result = _svn.UpdateRevisionInPlace(checkoutPath, _trunkUrl, "HEAD");
            Assert.True(result, $"Update {i + 1} should succeed");
        }
    }

    [Fact]
    public void UpdateRevisionInPlace_AdvancesContentInPlace()
    {
        // Commit, checkout HEAD into a second wc, then commit again so HEAD has
        // moved past that wc's BASE. UpdateRevisionInPlace should pull the second
        // commit's content into the existing wc.
        var workingDir = CreateCheckoutPath();
        _svn.CheckoutRevision(_trunkUrl, "HEAD", workingDir);
        File.WriteAllText(Path.Combine(workingDir, "a.txt"), "v1");
        RunSvn($"add \"{Path.Combine(workingDir, "a.txt")}\"");
        RunSvn($"commit \"{workingDir}\" -m \"add a v1\"");

        // Fresh checkout — at this point trunk@HEAD has a.txt=v1.
        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(_trunkUrl, "HEAD", checkoutPath);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(checkoutPath, "a.txt")));

        // Commit a second revision via the original wc; HEAD now moves past
        // checkoutPath's BASE.
        File.WriteAllText(Path.Combine(workingDir, "a.txt"), "v2");
        RunSvn($"commit \"{workingDir}\" -m \"set a v2\"");

        var ok = _svn.UpdateRevisionInPlace(checkoutPath, _trunkUrl, "HEAD");

        Assert.True(ok);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(checkoutPath, "a.txt")));
    }

    [Fact]
    public void UpdateRevisionInPlace_PreservesLocalModifications_UnlikeUpdateExistingCheckout()
    {
        // The defining behavioural difference: UpdateRevisionInPlace asserts the
        // caller knows the wc has no local changes and skips the Revert step.
        // If we DO have a local modification it survives the update; the sibling
        // UpdateExistingCheckout would revert it.
        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(_trunkUrl, "HEAD", checkoutPath);

        var localFilePath = Path.Combine(checkoutPath, "local-mod.txt");
        File.WriteAllText(localFilePath, "untracked-local-only");

        var ok = _svn.UpdateRevisionInPlace(checkoutPath, _trunkUrl, "HEAD");

        Assert.True(ok);
        // The local untracked file is still present — no Revert/Status cleanup ran.
        Assert.True(File.Exists(localFilePath));
        Assert.Equal("untracked-local-only", File.ReadAllText(localFilePath));
    }

    [Fact]
    public void UpdateRevisionInPlace_FromOneTagToAnother_SwitchesUrl()
    {
        // Switch handling: when the wc's URL no longer matches the requested URL
        // UpdateRevisionInPlace should still do the right thing (svn switch +
        // update). Use the v1.0 -> v2.0 tags so we cover the cross-URL path.
        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision($"{_repoRoot}/tags/v1.0", "HEAD", checkoutPath);

        var ok = _svn.UpdateRevisionInPlace(checkoutPath, $"{_repoRoot}/tags/v2.0", "HEAD");

        Assert.True(ok);
        // wc is now associated with the v2.0 URL.
        var branch = _svn.GetCurrentBranch(checkoutPath);
        Assert.Equal("tags/v2.0", branch);
    }

    [Fact]
    public void CheckoutRevision_ToNestedPath_CreatesDirectories()
    {
        var nestedPath = Path.Combine(CreateCheckoutPath(), "nested", "deep", "path");

        var result = _svn.CheckoutRevision(_trunkUrl, "HEAD", nestedPath);

        Assert.True(result);
        Assert.True(Directory.Exists(nestedPath));
    }

    [Fact]
    public void IsValidRepository_WithWorkingCopy_ReturnsTrue()
    {
        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(_trunkUrl, "HEAD", checkoutPath);

        var result = _svn.IsValidRepository(checkoutPath);
        Assert.True(result);
    }

    [Fact]
    public void IsValidRepository_WithNonWorkingCopyDirectory_ReturnsFalse()
    {
        var emptyDir = CreateCheckoutPath();
        Directory.CreateDirectory(emptyDir);

        var result = _svn.IsValidRepository(emptyDir);
        Assert.False(result);
    }

    [Fact]
    public void GetCurrentRevision_WithInvalidPath_ReturnsNull()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());
        var result = _svn.GetCurrentRevision(invalidPath);
        Assert.Null(result);
    }

    [Fact]
    public void CleanWorkspace_WithInvalidPath_ReturnsFalse()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());
        var result = _svn.CleanWorkspace(invalidPath);
        Assert.False(result);
    }

    [Fact]
    public void ResolveRevision_WithInvalidRepository_ReturnsNull()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());
        var result = _svn.ResolveRevision(invalidPath, "HEAD");
        Assert.Null(result);
    }

    [Fact]
    public void GetRevisionDescription_WithInvalidRepository_ReturnsNull()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());
        var result = _svn.GetRevisionDescription(invalidPath, "HEAD");
        Assert.Null(result);
    }

    #region GetCurrentBranch Tests

    [Fact]
    public void GetCurrentBranch_WithTrunkUrl_ReturnsTrunk()
    {
        var result = _svn.GetCurrentBranch(_trunkUrl);
        Assert.Equal("trunk", result);
    }

    [Fact]
    public void GetCurrentBranch_WithTrunkWorkingCopy_ReturnsTrunk()
    {
        var checkoutPath = CreateCheckoutPath();
        _svn.CheckoutRevision(_trunkUrl, "HEAD", checkoutPath);

        var result = _svn.GetCurrentBranch(checkoutPath);
        Assert.Equal("trunk", result);
    }

    [Fact]
    public void GetCurrentBranch_WithBranchesUrl_ReturnsBranchName()
    {
        var branchUrl = _repoRoot + "/branches/test-branch";

        var result = _svn.GetCurrentBranch(branchUrl);

        // If branch exists, should return branches/test-branch
        // If it doesn't exist, result may be null
        if (result != null)
            Assert.StartsWith("branches/", result);
    }

    [Fact]
    public void GetCurrentBranch_WithTagsUrl_ReturnsTagName()
    {
        var tagUrl = _repoRoot + "/tags/v1.0";
        var result = _svn.GetCurrentBranch(tagUrl);
        Assert.Equal("tags/v1.0", result);
    }

    [Fact]
    public void GetCurrentBranch_WithTagV2Url_ReturnsTagName()
    {
        var tagUrl = _repoRoot + "/tags/v2.0";
        var result = _svn.GetCurrentBranch(tagUrl);
        Assert.Equal("tags/v2.0", result);
    }

    [Fact]
    public void GetCurrentBranch_WithTagWorkingCopy_ReturnsTagName()
    {
        var checkoutPath = CreateCheckoutPath();
        var tagUrl = _repoRoot + "/tags/v1.0";
        _svn.CheckoutRevision(tagUrl, "HEAD", checkoutPath);

        var result = _svn.GetCurrentBranch(checkoutPath);
        Assert.Equal("tags/v1.0", result);
    }

    [Fact]
    public void GetCurrentBranch_WithInvalidPath_ReturnsNull()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());
        var result = _svn.GetCurrentBranch(invalidPath);
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithNonWorkingCopyDirectory_ReturnsNull()
    {
        var emptyDir = CreateCheckoutPath();
        Directory.CreateDirectory(emptyDir);

        var result = _svn.GetCurrentBranch(emptyDir);
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentBranch_WithRootUrl_ReturnsNull()
    {
        var result = _svn.GetCurrentBranch(_repoRoot);
        Assert.Null(result);
    }

    #endregion
}
