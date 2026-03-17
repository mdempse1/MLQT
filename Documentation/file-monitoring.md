# File Monitoring & Refresh

MLQT continuously watches your repository directories for file changes made outside the application. When you edit Modelica files in an external editor (such as Dymola, OpenModelica, or a text editor), MLQT detects those changes and lets you refresh the loaded libraries to stay in sync.

## How File Monitoring Works

### What Is Monitored

MLQT monitors each loaded repository's directory tree for:
- **Modelica files** (`.mo`) — The source files containing your models
- **Package order files** (`package.order`) — Files that define the ordering of child elements in packages
- **Directory changes** — New or deleted directories that may represent new or removed packages

Other file types (data files, images, documentation) are not monitored for the library browser, but are tracked separately by the [External Resources](external-resources.md) system.

### What Is Ignored

The file monitor skips:
- Hidden directories (`.git`, `.svn`, etc.)
- Non-Modelica file types
- Changes made by MLQT itself (e.g., when applying formatting rules)

### Debouncing

When a file is saved, the operating system often generates multiple change events in rapid succession (e.g., a temporary write followed by the final write). MLQT debounces these with a 500ms window — if the same type of change occurs on the same file within 500ms, only the last event is kept.

Different change types are not debounced against each other. For example, if a file is deleted and then re-created (as SVN does during some operations), both events are tracked and consolidated.

### Change Consolidation

Multiple changes to the same file are consolidated into a single net change:
- **Added then Deleted** = Removed from the list (no net change)
- **Added then Modified** = Kept as Added
- **Modified then Deleted** = Changed to Deleted
- **Deleted then Added** = Changed to Modified (the file was replaced)

## The Refresh Button

The **Refresh** button in the left panel toolbar is your primary way to process pending file changes.

### Visual Indicators

The Refresh button provides several visual cues:

| Indicator | Meaning |
|-----------|---------|
| **Red badge with number** | The count of pending file changes waiting to be processed |
| **Orange button color** | Changes are pending — a refresh is recommended |
| **Default button color** | No pending changes — everything is up to date |
| **Disabled button** | A refresh is currently in progress |

![Screenshot: Close-up of the Refresh button showing the red badge with "3" and the orange button color, indicating 3 pending changes.](Images/file-monitoring-1.png)

### Tooltip Details

Hover over the Refresh button to see a detailed breakdown of pending changes:
- "Refresh libraries (2 added, 3 modified, 1 deleted)" — Shows counts by change type
- "Refresh libraries" — No pending changes
- "Refreshing..." — A refresh is in progress

## What Happens During Refresh

When you click Refresh, MLQT processes all pending changes in this order:

1. **Deletions** — Removes deleted files from the library graph
2. **Renames** — Removes the old path and reloads the file from the new path
3. **Modifications** — Reloads modified files, re-parsing the Modelica code
4. **Additions** — Loads new files and parses them into the library graph
5. **Formatting** — If [Apply formatting rules](settings-reference.md#understanding-apply-formatting-rules) is enabled, applies formatting to changed files
6. **Dependency analysis** — Re-analyzes dependencies for affected models only (not the entire library)
7. **Style checking** — Re-runs style checks on affected models
8. **External resource analysis** — Re-analyzes external resource references for affected models
9. **Cleanup** — Clears the pending changes list and resets the badge

A success message appears when the refresh completes.

### Incremental Processing

The refresh process is **incremental** — it only processes the files that actually changed, not the entire library. This makes refreshes fast even for large libraries. Dependencies are re-analyzed only for models in the changed files and any models that depend on them.

## Automatic Refresh After VCS Operations

Some VCS operations automatically trigger a full refresh without you needing to click the button:

| Operation | Auto-Refresh |
|-----------|-------------|
| **Update (Pull)** | Yes — files may have changed from the remote |
| **Switch branch** | Yes — the entire working copy changes |
| **Merge** | Yes — merged files need re-analysis |
| **Rebase** | Yes — rebased files need re-analysis |
| **Revert** | Yes — reverted files need re-analysis |
| **Checkout revision** | Yes — all files may change |
| **Commit** | No — committing does not change file content |
| **Push** | No — pushing does not change local files |
| **Create branch** | No — creating a branch does not change files |

During automatic refreshes, the file monitor is temporarily paused to avoid detecting MLQT's own formatting writes as external changes.

## When to Refresh

### Typical Workflow

1. Edit Modelica files in your preferred editor
2. Save the files
3. Switch to MLQT and notice the red badge on the Refresh button
4. Click Refresh to process the changes
5. Review the updated code, issues, and dependencies

### You Don't Need to Refresh After...

- VCS operations performed within MLQT (update, switch, merge, etc.) — these auto-refresh
- Changing repository settings (style checking, formatting) — these trigger re-analysis automatically

### You Should Refresh When...

- The badge shows pending changes from external edits
- You've made changes in Dymola, OpenModelica, or a text editor
- You've manually modified files outside MLQT
- You want to pick up changes from a file synchronization tool

## Monitor Pause Behavior

The file monitor is automatically paused and resumed during certain operations to prevent false change detection:

- **During VCS operations**: Paused before the operation starts, resumed after the analysis pipeline completes
- **During formatting**: Paused before writing formatted files, resumed after all files are saved
- **During repository settings changes**: Paused if formatting settings change, resumed after reformatting completes

This ensures that MLQT's own file writes don't show up as pending external changes.
