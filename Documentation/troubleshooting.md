# Troubleshooting & FAQ

## Common Issues

### Repository Won't Load

**Symptom:** Adding a repository fails or shows an error message.

**Possible causes and solutions:**

| Cause | Solution |
|-------|----------|
| Path does not exist | Verify the path is correct and the directory exists on disk |
| No Modelica files found | The directory must contain at least one `package.mo` file for MLQT to recognize it as a Modelica library |
| Git not installed | For remote Git repositories, MLQT needs `git.exe` on your PATH for clone, fetch, and push operations. SVN does not require a command-line client. |
| Network issues | For remote repositories, check your network connection and verify the URL is correct |
| Permission denied | Ensure you have read access to the repository directory. For remote repositories, verify your credentials. |

### Parser Errors on Valid Code

**Symptom:** MLQT reports parser errors for Modelica code that compiles fine in Dymola or OpenModelica.

**Possible causes:**

- **Tool-specific extensions**: Dymola and OpenModelica support vendor-specific annotations and syntax extensions that are not part of the Modelica standard. MLQT's parser follows the standard grammar with some extensions to accept code that Dymola allows.  However, some vendor-specific syntax extensions may not be recognized.
- **Encoding issues**: MLQT expects UTF-8 encoded files. Files with other encodings may cause parsing failures.
- **Incomplete files**: If a file is being written by another tool while MLQT reads it, the parser may see incomplete content. Wait for the other tool to finish and click Refresh.

**What to do:** Parser errors appear in the Code Review issues table. Click the error to see details, including the line number and the offending token. These errors don't prevent MLQT from loading the rest of the library.

### Formatting Produces Unexpected Results

**Symptom:** After enabling "Apply formatting rules", the code is restructured in ways you didn't expect.

**Understanding:**

- Formatting **moves code** between sections — it doesn't change the code itself. If you see `import` statements that were in the middle of a section moved to the top, that's the "Imports first" rule working as intended.
- The **"One of each section"** rule merges multiple `public` or `protected` sections into one. If your code intentionally has multiple sections for organizational purposes, this rule will collapse them.
- The **"Format All Files"** button in the Edit Repository Details dialog reformats every file in the repository at once. This is intended for the initial formatting pass when first enabling formatting rules. After that, only modified files are reformatted.

**Recommendations:**

1. Enable formatting rules one at a time to understand each rule's effect
2. Use **Format All Files** on a new branch to do the initial formatting pass, then review the diff before merging
3. After the initial pass, commit changes with a message like "Apply MLQT formatting rules" to make clear the changes are structural, not functional
4. Discuss formatting rules with your team before enabling them on a shared repository

### Style Checking Reports Too Many Issues

**Symptom:** After enabling style rules, hundreds or thousands of issues appear.

**This is normal** for existing libraries that haven't been checked before. Common high-volume rules:

- **"Every class must have a description"** — Many classes lack descriptions
- **"Every public parameter must have a description"** — Parameters often lack descriptions
- **"Every class must have documentation info"** — Documentation is frequently missing

**Recommendations:**

1. Enable rules incrementally — start with one rule, fix the violations, then enable the next
2. Use the "Only this model" toggle in the issues table to focus on one model at a time
3. Use the search field to filter issues by type
4. Consider using less strict rules initially (e.g., just descriptions, not full documentation)

### Diff View Shows No Changes

**Symptom:** You know a file has been modified but the diff buttons are disabled.

**Possible causes:**

- **Local directory (no VCS)**: Diff only works with Git or SVN repositories. Local directories without VCS don't have a baseline to compare against.
- **File not committed yet**: If the file has never been committed, there is no HEAD version to diff against. The status will show "N" (untracked) or "A" (added).
- **Changes not detected yet**: The file monitor may not have picked up the change. Check the Refresh button for a pending changes badge and click it.

### SVN Branch Paths Look Wrong

**Symptom:** When viewing SVN diffs or history, file paths include branch prefixes like `trunk/` or `branches/feature-x/`.

**Understanding:** SVN stores branches as directories on the server, so file paths are relative to the repository root, not the working copy root. MLQT automatically strips common branch prefixes (`trunk/`, `branches/X/`, `tags/X/`) when matching files to your working copy. If your repository uses a non-standard directory layout, some paths may not be matched correctly.

### External Tool Check Hangs

**Symptom:** A Dymola or OpenModelica check starts but never completes.

**Possible causes:**

- **Tool not responding**: The simulation tool may have crashed or is waiting for user input in its own window
- **Port conflict**: Another process may be using the configured port. Try changing the port number in Settings > External Tools
- **License issue**: Dymola requires a valid license. If the license server is unreachable, Dymola may hang waiting for it.

**Solution:** Click **Stop** in the progress dialog to cancel the check. Check that the tool starts correctly outside of MLQT, then try again.

### Pending Changes Badge Not Updating

**Symptom:** You've edited files but the Refresh button doesn't show a badge.

**Possible causes:**

- **File type not monitored**: Only `.mo` files, `package.order` files, and directory changes are monitored. Changes to data files, images, or documentation don't trigger the badge.
- **Hidden directory**: Files inside `.git`, `.svn`, or other hidden directories are ignored.
- **Monitor paused**: During VCS operations, the file monitor is temporarily paused. It should resume automatically. If it doesn't, try switching away from the repository and back.

## Frequently Asked Questions

### Can I edit Modelica code in MLQT?

No. MLQT is a library management and quality tool, not a code editor. You edit code in your preferred editor (Dymola, OpenModelica, VS Code, etc.) and use MLQT to review, format, check, and commit changes. MLQT monitors your files for external changes and updates automatically.

### Do I need Git or SVN installed?

- **Git**: MLQT uses LibGit2Sharp (a built-in library) for local Git operations such as committing, branching, and reading history. However, operations that interact with a remote server — **fetch, push, rebase** — shell out to `git.exe` so that all configured credential helpers (Git Credential Manager, SSH keys, GitHub Desktop, etc.) are used automatically. You **must have Git installed** for these operations to work.
- **SVN**: MLQT uses SharpSvn (a built-in library) for all SVN operations, so you do **not** need the SVN command-line client installed.

### Can I use both Git and SVN repositories in the same project?

Yes. A project can contain any mix of Git, SVN, and local repositories. Each repository operates independently with its own settings.

### What happens if I change settings while others are using the same repository?

Repository settings are stored in `.mlqt/settings.json` inside the repository (see [Settings Reference: Where Settings Are Stored](settings-reference.md#where-settings-are-stored)). If you change settings and commit the file, other team members will get the updated settings when they pull/update. If they have MLQT open, they'll need to reload the repository to pick up the new settings.

### Can I use MLQT with libraries that aren't in version control?

Yes. When adding a repository, you can select a local directory that is not under Git or SVN. MLQT will load the libraries and provide all features except VCS operations (commit, revert, branch, merge, diff, history).

### How do I remove a repository from a project?

1. Go to **Settings > Manage Repositories**
2. Click on the repository row to open the Edit dialog
3. Click the **Delete Repository** button (red)

This removes the repository from MLQT's project — it does not delete any files on disk.

### Why are some formatting rules mutually exclusive?

**"Initial equation first"** and **"Initial equation last"** are contradictory. MLQT enforces mutual exclusivity — enabling one automatically disables the other.

### What's the difference between default style settings and repository settings?

- **Default style settings** (Settings > Style Checking) are a template used when adding new repositories. They do not affect existing repositories.
- **Repository settings** (Settings > Manage Repositories > click a repository) are the actual settings applied to that specific repository. They are stored in `.mlqt/settings.json` and can be shared via version control.

### Can multiple people use MLQT on the same repository simultaneously?

Yes. Each person runs their own instance of MLQT. VCS operations (commit, update, merge) are coordinated through the version control system as usual. The `.mlqt/settings.json` file is shared, so formatting and style rules are consistent across the team.

### Does MLQT modify my files without asking?

Only if you have **"Apply formatting rules"** enabled in the repository settings. When enabled, MLQT reformats files in these situations:

- **At startup** — Only files that VCS reports as modified, added, or untracked since the last commit. Unchanged committed files are assumed to be correctly formatted and are not touched.
- **Before opening the commit dialog** — Any modified files that haven't been formatted yet are formatted first.
- **After VCS operations that change files** (update, switch branch, merge, revert) — Only the files VCS reports as changed after the operation.
- **When you click Refresh** to process external file changes — Only the changed files.
- **When you click "Format All Files"** in the repository settings dialog — All files in the repository. Use this for the initial formatting pass when first enabling formatting rules.
- **When you apply repository settings that change formatting rules** — All files in the repository, since the rules themselves changed.

If "Apply formatting rules" is off (the default), MLQT **never modifies your files**. It only reads and analyzes them.
