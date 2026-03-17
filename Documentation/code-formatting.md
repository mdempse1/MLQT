# Code Formatting

MLQT can automatically apply formatting rules to Modelica source files. This page explains when formatting happens, what controls it, and how it interacts with VCS operations and external edits.

## Formatting Settings

Formatting behavior is controlled by the **Apply formatting rules** toggle in each repository's settings (see [Settings Reference](settings-reference.md#understanding-apply-formatting-rules)). When enabled, the following rules are applied during formatting:

| Setting | Effect |
|---------|--------|
| **One of each section** | Ensures each section type (declarations, equations, algorithms) appears at most once |
| **Import statements first** | Moves import statements to the top of the model |
| **Components before classes** | Sorts component declarations before nested class definitions |
| **Initial equation/algorithm first** | Positions initial equation/algorithm blocks at the start |
| **Initial equation/algorithm last** | Positions initial equation/algorithm blocks at the end |

If **Apply formatting rules** is disabled for a repository, MLQT will not modify any files in that repository during formatting operations.

Each repository can have its own independent formatting settings, allowing different formatting rules for different libraries.

## When Formatting Happens

Formatting is triggered in specific situations. MLQT does not continuously reformat files — it only formats at well-defined points in the workflow.

### On Application Startup

When MLQT starts (or when you switch projects), it formats any files that VCS reports as modified or untracked. This ensures that your working copy is consistently formatted before you begin working.

- Only files within the repository's Modelica library directory (`LocalPath`) are considered
- Each repository's own formatting settings are used
- Repositories with **Apply formatting rules** disabled are skipped entirely
- Files are identified via VCS status (modified, added, or untracked `.mo` files)

### After VCS Operations

The following VCS operations automatically trigger formatting of changed files:

| Operation | Formatting Applied |
|-----------|--------------------|
| **Update (Pull)** | Yes — incoming changes are formatted |
| **Switch branch** | Yes — all changed files are formatted |
| **Merge (Git/SVN)** | Yes — merged files are formatted |
| **Rebase** | Yes — rebased files are formatted |
| **Checkout revision** | Yes — checked-out files are formatted |
| **Revert** | **No** — reverted files preserve their committed content |
| **Commit** | Yes — before committing all modified files are formatted |
| **Push** | **No** — pushing does not change local files |
| **Create branch** | **No** — creating a branch does not change files |

Reverted files intentionally skip formatting to preserve the exact committed content. Reformatting a reverted file would create a dirty diff, defeating the purpose of the revert.

### Before Committing

When you click the **Commit** button, MLQT formats all modified files before opening the commit dialog. This ensures that committed code always follows the repository's formatting rules.

- Files that were already formatted (and not modified since) are skipped for efficiency
- The repository's own formatting settings are used
- If **Apply formatting rules** is disabled, no formatting occurs and the commit dialog opens immediately

### When Formatting Settings Change

If you change any formatting-related settings in a repository's configuration (such as enabling **Import statements first** or **Components before classes**), MLQT performs a full reformat of all files in that repository. This ensures the entire library is consistent with the new rules.

### Format All Files Button

The **Format All Files** button in repository settings forces a complete reformat of every file in the repository. Use this when:

- Setting up MLQT on an existing repository for the first time
- After changing formatting rules and wanting to ensure complete consistency
- After importing files from another source that may not follow your formatting conventions

This is the most thorough formatting operation — it rebuilds the entire file structure using the `ModelicaPackageSaver`, which can reorganize files into the correct package directory structure.

### On Manual Refresh

When you click the **Refresh** button to process pending file changes from external edits, formatting is applied to the changed files. Each repository's own formatting settings are used, so files from different repositories are formatted with the correct rules.

See [File Monitoring & Refresh](file-monitoring.md) for details on the refresh process.

## Excluding Models from Formatting

Individual models can be excluded from auto-formatting using the **FormatClear** toggle button in the Code Review toolbar. This is useful for models where the original author's formatting should be preserved, or where MLQT's formatting rules produce undesirable results.

### How It Works

- Toggle the FormatClear button (the "A" with a strikethrough) while a model is selected to exclude it from formatting
- Excluded models are stored in the `FormattingExcludedModels` list in the repository's `.mlqt/settings.json` file
- Excluded models skip the formatter during **all** formatting operations: startup formatting, VCS change formatting, pre-commit formatting, and Format All Files
- When you exclude a model that belongs to a VCS-tracked repository, MLQT reverts the model's file to undo any formatting changes that were already applied. This restores the file to its last committed state
- To re-include a model, select it and toggle the same button again. The model will be formatted on the next formatting pass

### Effect on Style Checking

Excluding a model from formatting affects which style violations are reported:

- **Formatting-related style rules are suppressed** for excluded models. These are the rules that correspond to formatting operations (section ordering, import placement, component ordering, etc.), since violations would be unfixable without the formatter
- **Non-formatting style rules still apply** to excluded models. This includes description checks, documentation checks, icon checks, naming convention checks, spell checking, and model reference validation

### When to Use

- When a model has intentional formatting that should not be changed (e.g., carefully aligned equations)
- When adopting MLQT on an existing repository and certain models need to remain unchanged
- When formatting a particular model causes undesirable structural changes

## When Formatting Does NOT Happen

Understanding when formatting is skipped is equally important:

- **External edits without refresh** — If you edit `.mo` files in an external editor, those changes are detected by the file monitor but not automatically formatted. You must click the Refresh button to process them.
- **Formatting disabled** — If **Apply formatting rules** is disabled for a repository, no formatting occurs for that repository regardless of the trigger.
- **Files outside the library directory** — The file monitor covers the VCS root path (which may be a parent of the Modelica library directory), but only files within `LocalPath` are formatted.
- **Files in hidden directories** — Files inside `.git`, `.svn`, or other hidden directories are never formatted.
- **Files not in the graph** — If a file has not been loaded into the library graph (e.g., a newly added file that hasn't been refreshed), it cannot be formatted by the incremental formatter. Use the Refresh button to load new files first.

## File Monitor Coordination

During formatting operations, the file monitor is temporarily paused to prevent MLQT's own file writes from being detected as external changes. The sequence is:

1. Pause the file monitor
2. Write formatted files
3. Clear any pending change events generated by the formatting writes
4. Resume the file monitor

This ensures that formatting does not create a feedback loop of detected changes.

## Incremental vs Full Formatting

MLQT uses two different formatting approaches depending on the situation:

### Incremental Formatting

Used for startup, VCS operations, pre-commit, and manual refresh. Only the specific changed files are parsed, rendered, and rewritten. This is fast and minimally disruptive.

### Full Formatting (Save All Libraries)

Used when formatting settings change or when the **Format All Files** button is clicked. The entire library is rebuilt through a four-phase process:

1. **Pre-parse** — All models are parsed in parallel
2. **Structure build** — The parent-child package tree is constructed
3. **Pre-render** — All models are rendered in parallel with the new formatting rules
4. **Write** — Files are written sequentially, and orphaned files (no longer needed) are cleaned up

Full formatting can also reorganize the file structure — for example, moving a model that was previously nested in a `package.mo` file into its own standalone file, or vice versa.
