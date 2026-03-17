# RevisionControl Skill

The RevisionControl project is a standalone, reusable library for integrating with version control systems.

**Location**: `RevisionControl/`

## Purpose

- Abstract version control operations for library comparison
- Enable comparing different revisions of a Modelica library from version control
- Provide a reusable component for Git and SVN operations

## Key Interface

```csharp
public interface IRevisionControlSystem
{
    // Checkout and workspace management
    bool CheckoutRevision(string repositoryPath, string revision, string outputPath);
    bool UpdateExistingCheckout(string checkoutPath, string repositoryPath, string revision);
    bool CleanWorkspace(string checkoutPath);

    // Repository queries
    bool IsValidRepository(string repositoryPath);
    string? GetCurrentRevision(string repositoryPath);
    string? GetCurrentBranch(string repositoryPath);
    string? GetRevisionDescription(string repositoryPath, string revision);
    string? ResolveRevision(string repositoryPath, string revision);

    // Log and history
    List<VcsLogEntry> GetLogEntries(string repositoryPath, VcsLogOptions? options = null);
    List<VcsChangedFile> GetChangedFiles(string repositoryPath, string revision);
    List<VcsWorkingCopyFile> GetWorkingCopyChanges(string repositoryPath);

    // Branch operations
    List<VcsBranchInfo> GetBranches(string repositoryPath, bool includeRemote = false);
    VcsOperationResult SwitchBranch(string repositoryPath, string branchName);
    VcsOperationResult CreateBranch(string repositoryPath, string branchName, bool switchToBranch = true);

    // Commit and sync
    VcsCommitResult Commit(string repositoryPath, string message, IEnumerable<string>? filesToCommit = null, IProgress<string>? progress = null);
    VcsUpdateResult UpdateToLatest(string repositoryPath);
    VcsOperationResult RevertFiles(string repositoryPath, IEnumerable<string> filesToRevert);
    VcsMergeResult MergeBranch(string repositoryPath, string sourceBranch);

    // Rebase operations (Git only)
    VcsOperationResult Rebase(string repositoryPath, string targetBranch);
    VcsOperationResult ContinueRebase(string repositoryPath);
    VcsOperationResult AbortRebase(string repositoryPath);

    // Push operations
    VcsOperationResult ForcePush(string repositoryPath);

    // Remote status
    bool IsBranchPushed(string repositoryPath);
    string? GetPullRequestUrl(string repositoryPath);

    // Conflict resolution
    VcsConflictVersions? GetConflictVersions(string repositoryPath, string filePath);

    // File content
    string? GetFileContentAtRevision(string repositoryPath, string filePath, string? revision = null);
}
```

## Git Implementation (GitRevisionControlSystem)

Uses LibGit2Sharp library.

```csharp
using RevisionControl;

var git = new GitRevisionControlSystem();

// Validate a repository
bool isValid = git.IsValidRepository(@"C:\Projects\MyRepo");

// Get current revision
string? currentCommit = git.GetCurrentRevision(@"C:\Projects\MyRepo");

// Resolve a revision reference to its full commit hash
string? commitHash = git.ResolveRevision(@"C:\Projects\MyRepo", "v1.0.0");

// Get a description of a revision
string? description = git.GetRevisionDescription(@"C:\Projects\MyRepo", "main");
// Returns: "Fix bug in parser (by John Doe on 2025-01-15)"

// Checkout a specific revision to a temporary directory
bool success = git.CheckoutRevision(@"C:\Projects\MyRepo", "v1.0.0", @"C:\Temp\checkout");
```

### Supported Git Revision Formats
- Commit hashes (full or partial)
- Branch names
- Tag names (lightweight and annotated)
- HEAD~N syntax
- Any Git revision specification

## SVN Implementation (SvnRevisionControlSystem)

Uses SharpSvn library.

```csharp
var svn = new SvnRevisionControlSystem();

// Validate an SVN repository (URL or working copy)
bool isSvnValid = svn.IsValidRepository("http://svn.example.com/repo/trunk");
bool isWorkingCopy = svn.IsValidRepository(@"C:\Projects\MySvnWorkingCopy");

// Get current revision of working copy
string? currentRev = svn.GetCurrentRevision(@"C:\Projects\MySvnWorkingCopy");

// Resolve a revision to its canonical form
string? revNum = svn.ResolveRevision("http://svn.example.com/repo/trunk", "HEAD");

// Checkout a specific SVN revision
bool success = svn.CheckoutRevision("http://svn.example.com/repo/trunk", "100", @"C:\Temp\svn_checkout");
```

### Supported SVN Revision Formats
- Revision numbers (e.g., 123)
- Keywords: HEAD, BASE, COMMITTED, PREV
- Repository URLs (http://, https://, svn://, file://)

## Workspace Reuse for Large Repositories

For large Modelica libraries, workspace reuse provides significant performance improvements.

### Traditional Approach (Slow)
```csharp
var git = new GitRevisionControlSystem();
using var comparer = new RevisionComparer(git);

// Each comparison checks out full repository from scratch
var result1 = comparer.CompareRevisions(repoPath, "v1.0.0", "v2.0.0", "package.mo");
var result2 = comparer.CompareRevisions(repoPath, "v2.0.0", "v3.0.0", "package.mo");
```

### Workspace Reuse Approach (Fast)
```csharp
var git = new GitRevisionControlSystem();

var workspaceDir = @"C:\Workspaces\modelica-lib";
using var comparer = new RevisionComparer(
    git,
    cleanupOnDispose: false,  // Keep workspaces for reuse
    workspaceDirectory: workspaceDir
);

// First comparison: Creates workspace, fetches objects (slower)
var result1 = comparer.CompareRevisions(repoPath, "v1.0.0", "v2.0.0", "package.mo");

// Second comparison: Reuses workspace, only fetches new objects (much faster!)
var result2 = comparer.CompareRevisions(repoPath, "v2.0.0", "v3.0.0", "package.mo");
```

### How Workspace Reuse Works
1. `UpdateExistingCheckout` initializes a Git repository in the workspace directory
2. Sets up remote pointing to source repository
3. Fetches only the needed commits (incremental)
4. Cleans workspace (hard reset + remove untracked files)
5. Checks out the requested revision

### Benefits
- First checkout creates full Git repository
- Subsequent checkouts only fetch new objects (much faster)
- Workspace cleaning ensures clean state between revisions
- Works seamlessly with switching between branches and tags
- Workspaces can be reused across multiple program runs

## RevisionComparer Integration

```csharp
using ModelicaComparer;
using RevisionControl;

var git = new GitRevisionControlSystem();
using var revisionComparer = new RevisionComparer(git);

// Compare two tagged releases
var result = revisionComparer.CompareRevisions(
    repositoryPath: @"C:\Projects\MyModelicaLib",
    oldRevision: "v1.0.0",
    newRevision: "v2.0.0",
    libraryPath: "MyLib/package.mo"
);

// Compare working directory to a branch
var result2 = revisionComparer.CompareWorkingDirectoryToRevision(
    repositoryPath: @"C:\Projects\MyModelicaLib",
    compareRevision: "main",
    libraryPath: "MyLib/package.mo"
);

// Access revision metadata
var oldRev = result.Properties["OldRevision"];
var newRev = result.Properties["NewRevision"];
var oldDesc = result.Properties["OldRevisionDescription"];
var newDesc = result.Properties["NewRevisionDescription"];
```

## Rebase Operations (Git Only)

```csharp
var git = new GitRevisionControlSystem();

// Rebase current branch onto target
var result = git.Rebase(repoPath, "main");

if (!result.Success && result.Message.Contains("conflict"))
{
    // Resolve conflicts, then continue
    var continueResult = git.ContinueRebase(repoPath);
    // Or abort
    var abortResult = git.AbortRebase(repoPath);
}

// Force push after successful rebase
var pushResult = git.ForcePush(repoPath);
```

## Remote Status and Pull Request URLs

```csharp
// Check if current branch has been pushed
bool pushed = git.IsBranchPushed(repoPath);

// Get PR creation URL (supports GitHub, GitLab, Bitbucket, Azure DevOps)
string? prUrl = git.GetPullRequestUrl(repoPath);
// Returns: "https://github.com/owner/repo/compare/feature-branch?expand=1"
```

## Conflict Resolution

```csharp
// Get conflict versions for a specific file
var versions = git.GetConflictVersions(repoPath, "path/to/file.mo");
if (versions != null)
{
    string baseContent = versions.Base;     // Common ancestor
    string oursContent = versions.Ours;     // Current branch
    string theirsContent = versions.Theirs; // Incoming branch
}
```

Git reads conflict entries from the index; SVN reads `.mine` / `.r{n}` sidecar files.

## Configurable SVN Branch Directories

SVN methods that interact with branch structure accept an optional `branchDirectories` parameter:

```csharp
var svn = new SvnRevisionControlSystem();

// Default: ["trunk", "branches", "tags"]
var branches = svn.GetBranches(repoPath);

// Custom layout
var customDirs = new List<string> { "main", "development", "releases" };
var branch = svn.GetCurrentBranch(repoPath, customDirs);
var log = svn.GetLogEntries(repoPath, options, customDirs);
var branches = svn.GetBranches(repoPath, customDirs);
svn.CreateBranch(repoPath, "new-branch", true, customDirs);
```

**Interpretation:** The first entry is the trunk equivalent (matched as a leaf path segment). Subsequent entries are branch containers (branches are found as immediate children). The static `DefaultBranchDirectories` property provides `["trunk", "branches", "tags"]`.

In the MLQT app, `StyleCheckingSettings.SvnBranchDirectories` stores the per-repository configuration, and `RepositoryService` passes it through to all SVN operations automatically.

## Implementing Other VCS Systems

To add support for Mercurial or other VCS:

1. Reference the RevisionControl project
2. Create a new class implementing `IRevisionControlSystem`
3. Implement all interface methods
4. Use with `RevisionComparer`

```csharp
public class HgRevisionControlSystem : IRevisionControlSystem
{
    public bool CheckoutRevision(string repositoryPath, string revision, string outputPath)
    {
        // Use Mercurial libraries or command-line
    }

    public string? GetCurrentRevision(string repositoryPath)
    {
        // Get current Mercurial revision
    }

    // ... implement other interface methods
}
```

## Dependencies

- **LibGit2Sharp** v0.31.0 - For Git repository operations
- **SharpSvn** v1.14005.390 - For SVN repository operations

## Key Files

- `RevisionControl/IRevisionControlSystem.cs` - Interface definition
- `RevisionControl/GitRevisionControlSystem.cs` - Git implementation
- `RevisionControl/SvnRevisionControlSystem.cs` - SVN implementation
- `RevisionControl.Tests/` - Comprehensive test coverage

## Testing

```bash
dotnet test RevisionControl.Tests
```

Tests create temporary Git/SVN repositories and clean them up automatically using `IDisposable`.

The SVN repositories dedicated to testing has the URL file:///C:/Projects/SVN/ModelicaEditorTest

The Git repositories dedicated to testing has the URL https://github.com/mdempse1/ModelicaEditorTests.git