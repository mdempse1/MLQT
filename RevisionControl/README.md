# RevisionControl

A .NET 10 library for integrating with Git and Subversion (SVN) version control systems. Provides a unified interface for repository operations including checkout, commit, branch management, history queries, and workspace reuse.

## Overview

RevisionControl abstracts version control operations behind the `IRevisionControlSystem` interface, with implementations for:

- **Git** via LibGit2Sharp
- **SVN** via SharpSvn

This enables higher-level tools (such as ModelicaComparer's `RevisionComparer`) to work with either VCS without knowing the implementation details.

## Key Concepts

### Unified Interface

All VCS operations go through `IRevisionControlSystem`, which covers:

- Repository validation and revision queries
- Checkout and workspace management
- Branch operations (list, switch, create)
- Commit history and file change tracking
- Commit, revert, update, and merge operations
- Rebase workflow (rebase, continue, abort) and force push
- Conflict resolution (get both sides of conflicted files)
- Push status and pull request URL construction
- File content retrieval at specific revisions

### Workspace Reuse

For large repositories, workspace reuse avoids re-cloning on every operation. `UpdateExistingCheckout` initializes a repository in the workspace directory, sets up a remote, and fetches only new objects incrementally.

## Usage

### Git Operations

```csharp
using RevisionControl;
using RevisionControl.Interfaces;

var git = new GitRevisionControlSystem();

// Validate a repository
bool isValid = git.IsValidRepository(@"C:\Projects\MyRepo");

// Get current state
string? revision = git.GetCurrentRevision(@"C:\Projects\MyRepo");
string? branch = git.GetCurrentBranch(@"C:\Projects\MyRepo");
string? description = git.GetRevisionDescription(@"C:\Projects\MyRepo", "main");
// Returns: "Fix bug in parser (by John Doe on 2025-01-15)"

// Resolve revision references
string? commitHash = git.ResolveRevision(@"C:\Projects\MyRepo", "v1.0.0");
```

### SVN Operations

```csharp
var svn = new SvnRevisionControlSystem();

// Works with URLs and working copies
bool isValid = svn.IsValidRepository("http://svn.example.com/repo/trunk");
bool isWc = svn.IsValidRepository(@"C:\Projects\MySvnWorkingCopy");

// Get revision info
string? rev = svn.GetCurrentRevision(@"C:\Projects\MySvnWorkingCopy");
string? branch = svn.GetCurrentBranch(@"C:\Projects\MySvnWorkingCopy");
```

### Checkout and Workspace Management

```csharp
var git = new GitRevisionControlSystem();

// Checkout a specific revision to a directory
bool success = git.CheckoutRevision(
    @"C:\Projects\MyRepo", "v1.0.0", @"C:\Temp\checkout");

// Reuse workspace for subsequent checkouts (much faster for large repos)
git.UpdateExistingCheckout(@"C:\Workspaces\mylib", @"C:\Projects\MyRepo", "v2.0.0");

// Clean a workspace (revert changes, remove untracked files)
git.CleanWorkspace(@"C:\Workspaces\mylib");
```

### Commit History

```csharp
// Get recent log entries
var options = new VcsLogOptions { MaxEntries = 20 };
List<VcsLogEntry> log = git.GetLogEntries(@"C:\Projects\MyRepo", options);

foreach (var entry in log)
{
    Console.WriteLine($"{entry.ShortRevision} {entry.Author}: {entry.MessageShort}");
    Console.WriteLine($"  Date: {entry.Date}");
}

// Get files changed in a specific commit
List<VcsChangedFile> changes = git.GetChangedFiles(@"C:\Projects\MyRepo", "abc123");
foreach (var file in changes)
    Console.WriteLine($"  {file.ChangeType}: {file.Path}");

// Get the commit date of the revision this branch was created from — i.e. the
// point on the parent branch where the new branch diverges. This is NOT the date
// of the copy operation itself: an 'svn copy' today may copy from a week-old
// trunk revision; this method returns the week-old date. SVN walks the branch
// with 'svn log --stop-on-copy' to find the copy-from revision. Git returns null.
DateTimeOffset? branchPoint = svn.GetBranchPointDate(
    @"https://svn.example.com/repo/releases/2026.1");

// Get uncommitted working copy changes
List<VcsWorkingCopyFile> wcChanges = git.GetWorkingCopyChanges(@"C:\Projects\MyRepo");
```

### Branch Operations

```csharp
// List branches
List<VcsBranchInfo> branches = git.GetBranches(@"C:\Projects\MyRepo", includeRemote: true);
foreach (var b in branches)
    Console.WriteLine($"{b.Name} {(b.IsCurrent ? "(current)" : "")}");

// Switch branch
VcsOperationResult result = git.SwitchBranch(@"C:\Projects\MyRepo", "feature/new-model");

// Create a new branch and switch to it
VcsOperationResult result = git.CreateBranch(
    @"C:\Projects\MyRepo", "feature/my-branch", switchToBranch: true);
```

### Commit, Update, Revert, Merge

```csharp
// Commit changes with progress reporting
var progress = new Progress<string>(msg => Console.WriteLine(msg));
VcsCommitResult commitResult = git.Commit(
    @"C:\Projects\MyRepo",
    "Fix model equations",
    filesToCommit: new[] { "Models/MyModel.mo" },
    progress: progress);

if (commitResult.Success)
    Console.WriteLine($"Committed: {commitResult.NewRevision}");

// SVN commits automatically detect missing files (deleted from disk but not
// yet scheduled for SVN deletion) and schedule them for deletion before commit.

// Update to latest from remote
VcsUpdateResult updateResult = git.UpdateToLatest(@"C:\Projects\MyRepo");

// Revert specific files
VcsOperationResult revertResult = git.RevertFiles(
    @"C:\Projects\MyRepo", new[] { "Models/MyModel.mo" });

// Merge a branch
VcsMergeResult mergeResult = git.MergeBranch(@"C:\Projects\MyRepo", "feature/other");
if (mergeResult.HasConflicts)
    Console.WriteLine($"Conflicts in: {string.Join(", ", mergeResult.ConflictedFiles)}");
```

### Rebase Operations (Git Only)

```csharp
// Rebase current branch onto main
VcsMergeResult rebaseResult = git.Rebase(@"C:\Projects\MyRepo", "main");

if (rebaseResult.HasConflicts)
{
    Console.WriteLine($"Conflicts in: {string.Join(", ", rebaseResult.ConflictedFiles)}");

    // Get both sides of a conflicted file for comparison
    var (ours, theirs) = git.GetConflictVersions(
        @"C:\Projects\MyRepo", @"C:\Projects\MyRepo\Models\MyModel.mo");

    // ... resolve conflicts, then continue
    VcsMergeResult continueResult = git.ContinueRebase(@"C:\Projects\MyRepo");

    // Or abort the rebase entirely to restore the pre-rebase state
    VcsOperationResult abortResult = git.AbortRebase(@"C:\Projects\MyRepo");
}

// After a successful rebase, force-push (uses --force-with-lease for safety)
VcsOperationResult pushResult = git.ForcePush(@"C:\Projects\MyRepo");
```

### Push Status and Pull Request URLs

```csharp
// Check if the current branch has been pushed to its remote
bool isPushed = git.IsBranchPushed(@"C:\Projects\MyRepo");

// Construct a pull request URL for the hosting service
// Supports GitHub, GitLab, Bitbucket, and Azure DevOps
string? prUrl = git.GetPullRequestUrl(@"C:\Projects\MyRepo", baseBranch: "main");
if (prUrl != null)
    Console.WriteLine($"Open PR at: {prUrl}");
```

### Conflict Resolution

```csharp
// During a merge or rebase with conflicts, retrieve both sides
var (ours, theirs) = git.GetConflictVersions(
    @"C:\Projects\MyRepo", @"C:\Projects\MyRepo\Models\MyModel.mo");

// For Git: reads blobs from the index conflict entry
// For SVN: reads .mine (ours) and highest-revision .r{n} sidecar files (theirs)
```

### File Content at Revision

```csharp
// Get file content at a specific revision
string? content = git.GetFileContentAtRevision(
    @"C:\Projects\MyRepo", "Models/MyModel.mo", "v1.0.0");

// Get last committed version (HEAD)
string? headContent = git.GetFileContentAtRevision(
    @"C:\Projects\MyRepo", "Models/MyModel.mo");
```

## Data Types

| Type | Purpose |
|------|---------|
| `VcsLogEntry` | Commit history entry (Revision, Author, Date, Message, Branch) |
| `VcsLogOptions` | Options for filtering log queries (MaxEntries, Since, Until, Branch) |
| `VcsChangedFile` | File changed in a commit (Path, ChangeType, OldPath) |
| `VcsWorkingCopyFile` | Uncommitted file change (Path, Status, IsStaged) |
| `VcsBranchInfo` | Branch metadata (Name, IsCurrent, IsRemote) |
| `VcsCommitResult` | Commit result (Success, NewRevision, ErrorMessage) |
| `VcsUpdateResult` | Update result (Success, HasChanges, OldRevision, NewRevision) |
| `VcsMergeResult` | Merge result (Success, HasConflicts, ConflictedFiles) |
| `VcsOperationResult` | Generic operation result (Success, ErrorMessage) |
| `VcsChangeType` | Enum: Added, Deleted, Modified, Renamed, Copied |
| `VcsFileStatus` | Enum: Modified, Added, Deleted, Renamed, Untracked, Conflicted |

## SVN Branch Directory Configurability

`SvnRevisionControlSystem` supports non-standard SVN repository layouts through configurable branch directory names. Several methods accept an optional `IReadOnlyList<string>? branchDirectories` parameter:

- `GetCurrentBranch(repositoryPath, branchDirectories)`
- `GetLogEntries(repositoryPath, options, branchDirectories)`
- `GetBranches(repositoryPath, includeRemote, branchDirectories)`
- `CreateBranch(repositoryPath, branchName, switchToBranch, branchDirectories)`

**Default:** `["trunk", "branches", "tags"]`

**How entries are interpreted:**
- The **first entry** is treated as the trunk equivalent and uses leaf matching (e.g., `/trunk` or `/trunk/` with no subdirectory)
- **Subsequent entries** are treated as branch containers whose subdirectories are individual branches (e.g., `/branches/feature-x`)

**Example:** For a repository using `trunk`, `tickets`, `dev`, and `release` as branch containers:

```csharp
var svn = new SvnRevisionControlSystem();
var dirs = new[] { "trunk", "tickets", "dev", "release" };

string? branch = svn.GetCurrentBranch(@"C:\Projects\MySvnWorkingCopy", dirs);
var branches = svn.GetBranches(@"C:\Projects\MySvnWorkingCopy", includeRemote: true, dirs);
```

When `branchDirectories` is `null`, the default `["trunk", "branches", "tags"]` is used, matching the standard SVN layout.

## Supported Revision Formats

### Git
- Commit hashes (full or partial)
- Branch names
- Tag names (lightweight and annotated)
- `HEAD~N` syntax
- Any Git revision specification

### SVN
- Revision numbers (e.g., 123)
- Keywords: HEAD, BASE, COMMITTED, PREV
- Repository URLs (http://, https://, svn://, file://)

## Implementing Other VCS Systems

To add support for Mercurial or another VCS:

```csharp
public class HgRevisionControlSystem : IRevisionControlSystem
{
    public bool IsValidRepository(string repositoryPath) { /* ... */ }
    public string? GetCurrentRevision(string repositoryPath) { /* ... */ }
    public bool CheckoutRevision(string repositoryPath, string revision, string outputPath) { /* ... */ }
    // ... implement all 19 interface methods
}
```

## License

MIT License — see [LICENSE](../LICENSE) for details.

## Dependencies

- **LibGit2Sharp** (v0.31.0) - .NET wrapper for libgit2
- **SharpSvn** (v1.14005.390) - .NET wrapper for Subversion
- **NLog** (v6.1.0) - Logging framework
