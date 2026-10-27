---
name: revision-control
description: "The RevisionControl library - Git/SVN integration, workspace reuse and revision comparison. Use when working on RevisionControl or on the VCS operations built on it."
---

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

`RevisionControl/Interfaces/IRevisionControlSystem.cs`, grouped here (the file's doc comments carry
each member's contract):

```csharp
public interface IRevisionControlSystem
{
    // Checkout and workspace management
    bool CheckoutRevision(string repositoryPath, string revision, string outputPath);
    bool UpdateExistingCheckout(string checkoutPath, string repositoryPath, string revision);
    bool UpdateRevisionInPlace(string checkoutPath, string repositoryPath, string revision);
    bool CleanWorkspace(string checkoutPath);

    // Repository queries
    bool IsValidRepository(string repositoryPath);
    string? FindRepositoryRoot(string path);
    string? GetCurrentRevision(string repositoryPath);
    string? GetCurrentBranch(string repositoryPath);
    string? GetDetachedHeadLabel(string repositoryPath);
    int CountCommitsOnNoBranch(string repositoryPath);
    string? GetRevisionDescription(string repositoryPath, string revision);
    string? ResolveRevision(string repositoryPath, string revision);
    string? GetPreviousRevision(string repositoryPath, string revision);
    DateTimeOffset? GetBranchPointDate(string repositoryPath);
    string? GetBranchPointRevision(string repositoryPath, string? startRevision = null);  // Git: null

    // Log and history
    List<VcsLogEntry> GetLogEntries(string repositoryPath, VcsLogOptions? options = null);
    List<VcsChangedFile> GetChangedFiles(string repositoryPath, string revision);
    IReadOnlyList<string>? GetChangedFilePathsSince(string repositoryPath, string sinceRevision);
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
    VcsOperationResult ResolveConflict(string repositoryPath, string filePath, ConflictResolutionChoice choice);

    // Rebase operations (Git only)
    VcsMergeResult Rebase(string repositoryPath, string targetBranch);
    VcsMergeResult ContinueRebase(string repositoryPath);
    VcsOperationResult AbortRebase(string repositoryPath);
    VcsRebaseInProgress? GetRebaseInProgress(string repositoryPath);  // a rebase left stopped (B382)

    // Push operations (SVN: no-op, a commit already reached the server)
    VcsOperationResult Push(string repositoryPath, string? branchName = null);
    VcsOperationResult ForcePush(string repositoryPath, string? branchName = null);

    // Remote status
    bool IsBranchPushed(string repositoryPath);
    string? GetPullRequestUrl(string repositoryPath, string? baseBranch = null);

    // Conflict resolution and file content - bytes, never text (B240, B264)
    (byte[]? ours, byte[]? theirs) GetConflictVersions(string repositoryPath, string filePath);
    byte[]? GetFileBytesAtRevision(string repositoryPath, string filePath, string? revision = null);
}
```

`RevisionControl/Interfaces/ILineLevelDiff.cs` is a second, narrower interface that only the Git
implementation has; a pull-request review needs it, which is why that review needs Git.

## Git Implementation (GitRevisionControlSystem)

Uses LibGit2Sharp for most operations, and the `git` executable (`RunGitCommand`) where libgit2 falls
short: fetch, rebase and its continue/abort, push, `update-index --refresh` and remote queries.

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

**Two path spaces reach the SVN layer, and nothing about a path says which it is in.** `svn log`
reports repository-root-relative paths (`trunk/Modelica/Foo.mo`); everything that works from the
checkout uses paths relative to the working copy. A repository registered at a library *inside* the
checkout has a `LocalPath` (the library) that differs from its `VcsRootPath` (the checkout), so
converting between the two by stripping a prefix and testing against the wrong root fails every time.
`GetFileBytesAtRevision` does not guess: when the working copy cannot answer it asks the server —
the repository root first, then the working copy's own URL (`ContentUrlCandidates`), pegged at the
revision asked for — which also covers a revision newer than the working copy and a file deleted
since (B265). `svn cat` is the only command read through `SvnCli.RunForBytes`, because it writes a
file whose encoding is the file's own; every other command's output is text svn generated itself,
which is UTF-8 (B264).

### Commands that stall, and what a stopped one leaves behind

An svn command MLQT runs is stopped when it has been silent for its limit: 10 minutes for anything
that writes the working copy (`SvnCli.IdleTimeout`), 2 minutes for history walks
(`HistoryIdleTimeout`), 30 seconds for small queries (`QueryIdleTimeout`). A git command is stopped
when it has run for `GitCommandTimeout` (15 minutes) in all. Three things follow, and each has cost
a defect:

- **The svn limit watches output, so never make an svn command quiet.** `svn update --quiet` on a
  large working copy prints nothing and looked exactly like a stalled server (B383).
  `SvnCommandRunnerTests.NoSvnCommandIsMadeQuiet` reads RevisionControl's source for it. **For the
  same reason, the output is read on threads of its own, not with `ReadAsync` on the pool.** On a busy
  pool the reads queue behind other work, output sits unread in the pipe, and a command that is still
  writing looks silent. `SvnCommandRunnerStarvationTests` fills a capped pool to hold that.
- **A stop is a kill, and a killed command leaves its lock.** Git leaves `.git/index.lock` (a killed
  `update-index --refresh` is enough), and every later operation fails with "File exists". svn leaves
  the working copy locked (E155004). `RunGitCommand` removes only the `*.lock` files written since the
  command started (`RemoveLocksLeftBehind`), because an older one may belong to another git that is
  still working. Every svn command that writes the working copy goes through `SvnCli.RunOnWorkingCopy`,
  which follows a stopped command with `svn cleanup`. A new writing command must use it too (B330).
- **Report svn's own words.** A failure message is `SvnCli.Result.FailureMessage(fallback)`: stderr
  when there is any, the fallback only when there is none. Authentication errors and "run 'svn cleanup'"
  reach the user only this way (B329).

Where a changed-files lookup pegs its URL is a trade-off (`PegForChangedFiles`, B386). Unpegged, the
URL is pegged at HEAD, and a working copy whose branch has since been deleted or renamed has nothing
at its URL there. Pegged at the revision asked for, svn cannot follow copy history back to a revision
from before the branch existed. So the URL is pegged at the later of the working copy's revision and
the one asked for.

### Supported SVN Revision Formats
- Revision numbers (e.g., 123)
- Keywords: HEAD, BASE, COMMITTED, PREV
- Repository URLs (http://, https://, svn://, file://)

## Workspace Reuse for Large Repositories

For large Modelica libraries, reusing one checkout across revisions is much faster than checking out
from scratch each time. `UpdateExistingCheckout` (and `UpdateRevisionInPlace`) are the members for
it; the revision-comparison tool that drives them lives outside this repository, so nothing in MLQT
shows the pattern in code.

### How Workspace Reuse Works (Git)
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

## Rebase Operations (Git Only)

```csharp
var git = new GitRevisionControlSystem();

// Rebase current branch onto target
var result = git.Rebase(repoPath, "main");

if (result.HasConflicts)   // a VcsMergeResult: ConflictedFiles lists them
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
var branches = svn.GetBranches(repoPath, includeRemote: false, customDirs);
svn.CreateBranch(repoPath, "new-branch", true, customDirs);
```

These overloads are on `SvnRevisionControlSystem` only, not on the interface; the interface members
use the default layout.

**Interpretation:** The first entry is the trunk equivalent (matched as a leaf path segment). Subsequent entries are branch containers (branches are found as immediate children). A null list means the default, the private `DefaultBranchDirectories`: `["trunk", "branches", "tags"]`.

In the MLQT app, `StyleCheckingSettings.SvnBranchDirectories` stores the per-repository configuration, and `RepositoryService` passes it through to all SVN operations automatically.

## Implementing Other VCS Systems

To add support for Mercurial or other VCS:

1. Reference the RevisionControl project
2. Create a new class implementing `IRevisionControlSystem`
3. Implement all interface methods (and `ILineLevelDiff` if it can answer line-level diffs)
4. Teach `MLQT.Services/RepositoryService.cs`, which chooses between the Git and SVN implementations, about it

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

- `RevisionControl/Interfaces/IRevisionControlSystem.cs` - Interface definition
- `RevisionControl/Interfaces/ILineLevelDiff.cs` - Line-level diff, Git only
- `RevisionControl/DataTypes/` - Result and log types (`VcsMergeResult`, `VcsLogEntry`, `RevisionId`, ...)
- `RevisionControl/GitRevisionControlSystem.cs` - Git implementation
- `RevisionControl/SvnRevisionControlSystem.cs` - SVN implementation
- `RevisionControl/SvnCli.cs` - Runs svn: idle limits, `RunOnWorkingCopy`, `RunForBytes`, XML parsing
- `RevisionControl/SvnToolLocator.cs` - Resolves `MLQT_SVN_PATH`, the bundled client or `svn` on PATH
- `RevisionControl/VcsRelativePath.cs`, `RevisionControlLogger.cs` - Path helper and the assembly's logging hook
- `build/fetch-svn-tools.ps1` - Stages the SlikSVN client for bundling
- `svn-tools/README.md` - How the bundled client is populated and shipped
- `RevisionControl.Tests/` - Comprehensive test coverage

## Testing

```bash
dotnet test RevisionControl.Tests
```

Tests create temporary Git/SVN repositories and clean them up automatically using `IDisposable`.

SVN tests never use a fixed repository: `SvnTestRepository` (`TestSupport/`, linked into
`RevisionControl.Tests` and `MLQT.Services.Tests`) builds one per run with `svnadmin create`
(trunk with a small `ModelicaEditorTest` library, tags `v1.0`/`v2.0`, `branches/feature-test`) and
deletes it on disposal. The shared repository and working copy the tests once kept at a fixed path
on the developer's machine are retired: the tests commit, so two runs at once moved HEAD under each
other (B426), and `RepositoryServiceTests`, the last to use it, asserted nothing anywhere else (B471). `SvnWorkingCopyFixture` gives the classes CI runs a trunk working copy, or
none where svn is not installed.

Git tests never clone either: `GitTestRepositoryFixture` (`RevisionControl.Tests`) builds the same
layout with LibGit2Sharp (`main` with three commits, tags `v1.0.0`/`v2.0.0`, `feature-test`) and
**throws when it cannot**, so a broken fixture fails every test using it. It used to catch the
exception, and about 187 tests returned early on it having asserted nothing; `GitIntegrationTests`
cloned https://github.com/mdempse1/ModelicaEditorTests.git for every test and did the same offline
(B481). No test uses that repository now. A test that needs a remote clones the fixture locally.
**A condition the fixture guarantees is asserted, never returned on.** The one skip left is
`SvnWorkingCopyFixture.RequireWorkingCopy()`, the `Assert.SkipUnless` every class on that fixture
calls where svn is absent - reported as skipped, not passed (B481, B486). The repository is the
run's own, so its answers are known and asserted: trunk's current revision is
`SvnTestRepository.TrunkLastChangedRevision` (7), HEAD is `HeadRevision` (10), and a test that
asserts only that an answer came back is asserting less than the fixture makes possible.