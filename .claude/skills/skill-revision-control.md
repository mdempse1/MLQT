# RevisionControl Skill

The RevisionControl project is a standalone, reusable library for integrating with version control systems.

**Location**: `RevisionControl/`

## Purpose

- Abstract version control operations for library comparison
- Enable comparing different revisions of a Modelica library from version control
- Provide a reusable component for Git and SVN operations

**It has no project references, deliberately** — it is the one assembly that knows nothing about
Modelica. So file content at a revision, and a conflict's two sides, come back as **bytes**
(`GetFileBytesAtRevision`, `GetConflictVersions`), and MLQT decodes them in
`MLQT.Services.Helpers.VcsFileText` through the same `ModelicaFileEncoding` funnel it uses on disk
(B240). A version control system stores bytes and what they mean is the caller's question; decoding
inside this assembly (`File.ReadAllText` on SVN's sidecars, `Blob.GetContentText()` in Git) showed a
Windows-1252 library's accented characters as replacement characters. Do not add a member that
returns a file's content as a string.

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
    VcsRebaseInProgress? GetRebaseInProgress(string repositoryPath);  // a rebase left stopped (B382)

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

Performs **all** operations through the `svn` command-line client via the `SvnCli` helper
(`RevisionControl/SvnCli.cs`) — the managed SharpSvn library has been removed from the
shipped product. The CLI is ~14x faster than SharpSvn on large working copies (~2.7s vs
~38s for `svn update` on a 30,000-file library — SharpSvn's cost was per-file managed
interop). `SvnCli` invokes svn via `ProcessStartInfo.ArgumentList` (runtime-handled
quoting), appends `--non-interactive`, parses `--xml` output, and reads stdout/stderr
concurrently to avoid pipe deadlock. `SvnToolLocator` picks the executable: `MLQT_SVN_PATH`
env var, then the bundled SlikSVN client at `{AppContext.BaseDirectory}/svn/svn.exe`, then
`svn` on PATH. There is no managed fallback — an `svn` executable must be resolvable or
`SvnCli` raises an error. The bundled binaries are staged by `build/fetch-svn-tools.ps1`
into `svn-tools/win-x64` at the repository root and copied to the app output by
`MLQT.Photino.csproj`, under a Windows-only condition (on Linux the `.deb` declares
`subversion` instead). `BundledSvnClientTests` holds that chain together.

SharpSvn remains only as a **test-only** dependency of `RevisionControl.Tests` (used to set
up and validate repository state in the integration tests), not of the shipped product.

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

// A rebase stopped on conflicts stays stopped after the dialog closes, with HEAD detached. This is
// how anything opened later finds it - the branch being rebased and the files still in conflict
// (empty once resolved, which is when Continue is offered). The rebase dialog opens on it (B382).
var stopped = git.GetRebaseInProgress(repoPath);

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
var (ours, theirs) = git.GetConflictVersions(repoPath, fullPathToFile);  // bytes (B240)
// ours   = the user's own branch - what KeepMine keeps
// theirs = the other branch      - what AcceptIncoming takes

git.ResolveConflict(repoPath, fullPathToFile, ConflictResolutionChoice.KeepMine);
```

Git reads conflict entries from the index; SVN reads `.mine` / `.r{n}` sidecar files.

**"Mine" is the user's branch in a rebase too, which is the reverse of git (B418).** A rebase
replays the user's commits onto the other branch, so HEAD (index stage 2, git's "ours") is the
branch being rebased onto and the replayed commit is stage 3 (git's "theirs"), and there is no
`MERGE_HEAD`. During a rebase `ResolveConflict` takes Keep Mine / Accept Incoming from those index
stages, and `GetConflictVersions` swaps the pair, so both the buttons and the diff mean the same
thing in a merge and a rebase. Checking out `MERGE_HEAD`/`HEAD` as a merge does made Accept
Incoming fail and Keep Mine discard the user's change.

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
- **Bundled SlikSVN CLI** (not a NuGet package) - svn.exe used for **all** SVN operations; staged by `build/fetch-svn-tools.ps1`
- **SharpSvn** v1.14005.390 - **test-only** dependency of `RevisionControl.Tests` (not referenced by the shipped product)

## Key Files

- `RevisionControl/IRevisionControlSystem.cs` - Interface definition
- `RevisionControl/GitRevisionControlSystem.cs` - Git implementation
- `RevisionControl/SvnRevisionControlSystem.cs` - SVN implementation
- `RevisionControl/SvnToolLocator.cs` - Resolves the bundled/PATH svn.exe for CLI operations
- `build/fetch-svn-tools.ps1` - Stages the SlikSVN client for bundling
- `svn-tools/README.md` - How the bundled client is populated and shipped
- `RevisionControl.Tests/` - Comprehensive test coverage

## Testing

```bash
dotnet test RevisionControl.Tests
```

Tests create temporary Git/SVN repositories and clean them up automatically using `IDisposable`.

The SVN repositories dedicated to testing has the URL file:///C:/Projects/SVN/ModelicaEditorTest

The Git repositories dedicated to testing has the URL https://github.com/mdempse1/ModelicaEditorTests.git