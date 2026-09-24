# Code Formatting

MLQT can automatically apply formatting rules to Modelica source files. This page explains when formatting happens, what controls it, and how it interacts with VCS operations and external edits.

## Formatting Settings

Formatting is controlled by seven switches in each repository's settings, under **Formatting rules**. Open them with **Settings > Manage Repositories**, then click the repository's row.

![Screenshot: The "Formatting rules" section of the Edit Repository Details dialog, showing seven toggle switches: "Apply formatting rules", "A class may only have 1 public, 1 protected, 1 equation or algorithm section", "Composition must be imports first; then extends at the top of the public/protected sections", "Composition must have components before classes", "Declarations in order: inputs and outputs, constants, parameters, variables, components", and the two initial equation/algorithm ordering switches.](Images/code-formatting-1.png)

The first is the master switch; the other six say what "formatted" means for this repository. **The labels below are the dialog's own**, so you can match what you are reading to what is on the screen.

| Switch | Rule id | Settings key | What the formatter does |
|--------|---------|--------------|-------------------------|
| **Apply formatting rules. If off then just used as part of style guidelines** | — | `ApplyFormattingRules` | The master switch. Off, MLQT reports layout but never rewrites a file. See [Understanding "Apply Formatting Rules"](settings-reference.md#understanding-apply-formatting-rules) |
| **A class may only have 1 public, 1 protected, 1 equation or algorithm section** | `MLQT.Style.OneOfEachSection` | `OneOfEachSection` | Merges multiple sections of the same kind into one |
| **Composition must be imports first; then extends at the top of the public/protected sections** | `MLQT.Style.ImportStatementsFirst` | `ImportStatementsFirst` | Moves `import` statements to the top of each section, then `extends` clauses |
| **Composition must have components before classes** | `MLQT.Style.ComponentsBeforeClasses` | `ComponentsBeforeClasses` | Sorts component declarations before nested class definitions. Only does anything when *imports first* is also on |
| **Declarations in order: inputs and outputs, constants, parameters, variables, components** | `MLQT.Style.DeclarationOrder` | `DeclarationOrder` | Sorts the declarations within that group. Only does anything when *components before classes* is also on |
| **If there is an initial equation/algorithm section it should appear before the equation/algorithm section** | `MLQT.Style.InitialEqAlgoFirst` | `InitialEQAlgoFirst` | Writes `initial equation` and `initial algorithm` blocks before the regular ones |
| **If there is an initial equation/algorithm section it should appear after the equation/algorithm section** | `MLQT.Style.InitialEqAlgoLast` | `InitialEQAlgoLast` | Writes them after the regular ones |

The two initial-section switches are mutually exclusive: turning one on turns the other off. The
last three rows are a chain of refinements rather than alternatives: *components before classes*
refines *imports first*, and *declarations in order* refines that, and the formatter consults each
only inside the branch the one above it selects — so on its own, each changes nothing.
[Settings Reference](settings-reference.md#formatting-rules) has the same seven rows with their
defaults and the full description of each.

**Declarations in order needs to know a variable from a component**, and that is not something the
Modelica grammar answers: `Real x` is a variable and `Resistor r` is a component, but `SI.Length x`
is a variable by every convention while its type is a class. MLQT follows the declared type through
its alias chain, which needs the library loaded — so a check with nothing loaded recognises only
`Real`, `Integer`, `Boolean` and `String` as variables and leaves everything else where a component
goes. **The formatter is told exactly the same thing**, so what it writes is always what the rule
asks for.

**One of each section is the master switch for layout.** With it off the formatter writes the class in
source order and moves nothing at all — so the other switches are **switched off with it**, both as
formatting transforms and as style rules. Enabling *imports first* on its own would report
an arrangement the formatter could never produce, so MLQT does not let you: the switches are greyed
out in repository settings, and a hand-edited settings file gets a warning from `mlqt check`. See
[One of each section is required by the rest](settings-reference.md#one-of-each-section-is-required-by-the-rest).

### Writing the settings by hand

A repository's settings live in `.mlqt/settings.json`, committed with the code, and `mlqt check` reads the same file — so a repository that has never been opened in the desktop application can still be checked in CI. Each switch above can be written as its **Settings key**:

```json
{
    "ApplyFormattingRules": true,
    "OneOfEachSection": true,
    "ImportStatementsFirst": true,
    "ComponentsBeforeClasses": false,
    "DeclarationOrder": false,
    "InitialEQAlgoFirst": true,
    "InitialEQAlgoLast": false
}
```

Two things to watch when writing this by hand:

- **The key and the rule id are not spelled the same.** The key is `InitialEQAlgoFirst` with a capital `EQ`; the rule id is `MLQT.Style.InitialEqAlgoFirst`. The keys are what `settings.json` uses; the rule ids are what findings, `RuleSeverities` and `__MLQT(suppress="…")` use.
- **A severity written against one of these does nothing.** These six are switches, not Off/Info/Warning/Error rows, and their level is worked out rather than chosen: a layout finding is a **warning** when *Apply formatting rules* is off and an **error** when it is on. Writing `"MLQT.Style.OneOfEachSection": "Error"` in `RuleSeverities` records only that the rule is on — the value is not read. See [How severely these are reported](settings-reference.md#how-severely-these-are-reported).

> **Formatting and checking agree about these.** Each row above is also a style rule, and the
> formatter writes what the rule asks for. That was not always true of *Initial equation/algorithm
> last*: the formatter used to write initial blocks first whatever the setting said, so a repository
> that chose "last" had the finding reintroduced on every save and the rule could never be satisfied.
> It now writes them where you asked.
>
> Because the two agree, a layout finding that survives formatting is reported as an **error** rather
> than a warning — see [How severely these are reported](settings-reference.md#how-severely-these-are-reported).

If **Apply formatting rules** is disabled for a repository, MLQT will not modify any files in that repository during formatting operations.

Each repository can have its own independent formatting settings, allowing different formatting rules for different libraries.

## When Formatting Happens

Formatting is triggered in specific situations. MLQT does not continuously reformat files — it only formats at well-defined points in the workflow.

### On Application Startup

When MLQT starts (or when you switch projects), it formats any files that VCS reports as modified or untracked. This ensures that your working copy is consistently formatted before you begin working.

- Only files within the repository's specified directory (`LocalPath`) are considered
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

The **Format All Files** button in repository settings forces a complete reformat of every file in the repository. It is disabled while **Apply formatting rules** is off. Use this when:

- Setting up MLQT on an existing repository for the first time
- After changing formatting rules and wanting to ensure complete consistency
- After importing files from another source that may not follow your formatting conventions

This is the most thorough formatting operation, and the one that restructures the repository on disk: it writes every package as a directory with one file per class. See [One File Per Class](#one-file-per-class) — the first run on a single-file library produces a very large commit.

### On Manual Refresh

When you click the **Refresh** button to process pending file changes from external edits, formatting is applied to the changed files. Each repository's own formatting settings are used, so files from different repositories are formatted with the correct rules.

See [File Monitoring & Refresh](file-monitoring.md) for details on the refresh process.

## One File Per Class

MLQT stores a library the way Modelica's own directory mapping describes it: **a package is a directory**, holding a `package.mo` for the package itself, **one `.mo` file per class inside it**, and a `package.order` naming them in order.

If a library is currently one `.mo` file holding dozens of classes, a full format **expands it**. `Lib.mo` becomes `Lib/package.mo`, `Lib/Resistor.mo`, `Lib/Capacitor.mo` and so on; nested packages become nested directories; and the original single file is deleted once everything in it has been written somewhere else.

> **Expect a very large commit the first time.** One file disappears and dozens appear in its place. Do it deliberately — on a clean working copy, as a commit of its own, at a moment when nobody has a long-running branch open — and say so in the commit message. It is a move, not a rewrite, but version control cannot tell until you commit it.

### Why it is worth it

The reason is version control, and it is the same reason MLQT formats at all: so that a diff shows the change and nothing else.

- **A diff is per class.** A review shows which models were touched, rather than one enormous file with edits scattered through it.
- **History is per class.** `git log Lib/Resistor.mo` — or `svn log` on the same path — answers *when did this model last change, and why* directly. In a single-file library every class shares one history, and that question cannot be asked at all.
- **Blame points at the model.** The last person to touch a class is the last person to touch its file, not the last person to touch anything in the library.
- **Merges conflict less.** Two people working on different models in the same package are no longer editing the same file.

### What stays inside `package.mo`

Not every class can have a file of its own, and MLQT leaves those inline in the parent package:

- classes carrying an element prefix — `replaceable`, `redeclare`, `inner`, `outer` — which Modelica only permits inside their parent;
- short class definitions (`package Types = Modelica.Units.SI;`), which are written as a single line rather than as a directory;
- a class whose **directory entry** would collide with a sibling's or with a file the package already
  has. A package is written as a directory `Name` and every other class as a file `Name.mo`, so the
  collision is between those, not between the class names: two models called `JFET` and `Jfet` would
  both want `Jfet.mo` and both stay inline, but a model `JFET` beside a *package* `Jfet` is a file
  next to a directory and both are written out. For the same reason a model called `Package` stays
  inline — it would be written as `Package.mo`, which is the `package.mo` the directory already has —
  while a *package* called `Package` becomes the directory `Package` and does not collide.

### When the restructure happens

Only on the **full** library save: the [Format All Files](#format-all-files-button) button, and the automatic full reformat that runs when you change a repository's formatting settings.

The incremental path — at startup, after a VCS operation, and after a refresh — rewrites the files a change touched **in place**. It never moves a class from one file to another, so day-to-day work does not quietly restructure your repository.

### A package that arrives from another tool

This is the case the two paragraphs above leave open, and it is a common one. You create a package in
Dymola or another editor, and it saves the whole thing as a single `.mo` file inside your repository.

MLQT **will not restructure it on its own**. The next time you open the project the file is
VCS-modified, so the incremental path reformats it in place — which means MLQT visibly touches the
file and still leaves it as one file. Nothing moves it to a directory until you run
[Format All Files](#format-all-files-button), or change a formatting setting and let the full
reformat run.

What you get instead is a finding. **`MLQT.Structure.SingleFilePackage` is on by default**, at
Warning, precisely so this does not pass unnoticed:

```
package Pumps is stored as a single file; its 4 classes could each have a file of their own
```

**Fix it from the finding.** The row in Code Review carries a **Split into files** action, and so
does the Finding Details dialog. It writes that one package as a directory with a file per class and
a matching `package.order`, deletes the single file it came from, and leaves the rest of the
repository alone — which is the difference between it and **Format All Files**, where correcting one
package means rewriting every file in the library. It asks before it does it, since it creates a
directory and deletes a file.

Nothing else moves: the parent package's `package.order` already names this package and still does,
because what changes is where the package is stored, not what it is called.

See [settings-reference.md](settings-reference.md#where-the-rules-live) for turning the rule off if
your repository keeps packages single-file on purpose.

A file that version control reports as newly **Added** is never removed by the tidy-up that follows a save, so a class you have created but not yet committed cannot be lost to it.

## Line endings and how files end

**A file is written back with the line endings it already had.** A Windows checkout is CRLF, a Linux
one is LF, and formatting either leaves it as it was — the same rule MLQT applies to a file's
character encoding, and for the same reason: how your files are stored is your repository's business,
not the formatter's.

This was not always true, and the symptom was confusing enough to be worth naming. Formatting used to
write LF whatever the file was, so on Windows **every file in the library showed as modified while
`git diff` reported no differences at all**. Both were right: with `core.autocrlf=true` git converts
CRLF away before comparing, so the command line saw nothing, while MLQT compares the bytes and saw
every file change. If you are upgrading from a version before this was fixed and your working copy is
full of files with no visible differences, discard them — a fresh format will leave the library
alone.

### How files end

Every `.mo` and `package.order` file MLQT writes ends with a newline, whichever path wrote it. This matters more than it sounds: the two paths used to disagree, so a library formatted incrementally and later put through **Format All Files** came back with every file modified and nothing changed in any of them — a commit of thousands of empty diffs with any real change buried inside it.

If you are upgrading from a version before this was fixed, expect **one** such commit: the files gain the newline they were missing, once, and are stable afterwards. Committing that on its own, before making any other change, keeps it out of the way of a review.

## Excluding Models from Formatting

Individual models can be excluded from auto-formatting using the **FormatClear** toggle button in the Code Review toolbar. This is useful for models where the original author's formatting should be preserved, or where MLQT's formatting rules produce undesirable results.

### How It Works

- Toggle the FormatClear button (the "A" with a strikethrough) while a model is selected to exclude it from formatting
- **The exclusion is written into the class itself**, as `annotation(__MLQT(format=false))` — so it travels with the class when it is renamed or moved, and it is committed alongside the code it applies to. See [In the source instead](#in-the-source-instead-__mlqtformatfalse) below for what the directive means everywhere else
- Excluded models skip the formatter during **all** formatting operations: startup formatting, VCS change formatting, pre-commit formatting, and Format All Files
- When you exclude a model that belongs to a VCS-tracked repository, MLQT first reverts the model's file to undo any formatting changes that were already applied — restoring it to its last committed state — and then writes the annotation. The file is therefore modified afterwards, by that one line
- To re-include a model, select it and toggle the same button again: the directive is removed, along with the annotation itself if that is all it held. The model will be formatted on the next formatting pass

Earlier versions of MLQT recorded the exclusion as a class name in the `FormattingExcludedModels`
list in `.mlqt/settings.json` instead. That list is still honoured, so nothing you excluded before
has changed, and re-including a class clears it from the list as well as from the source. It is no
longer what the button writes, because a name in a settings file does not survive the class being
renamed — the entry stays behind naming nothing and the class quietly comes back under the
formatter.

### In the source instead: `__MLQT(format=false)`

This is what the toolbar button writes. It is also worth writing by hand when you want to record a
reason, which the button cannot ask you for:

```modelica
model Rectifier "Order matters to the solver"
  // ...
  annotation(__MLQT(preserveOrder = true,
                    reason = "declaration order affects the nonlinear system"));
end Rectifier;
```

`format = false` is a synonym. Either one:

- **takes the class out of every formatting pass**, exactly as the name list does — startup, VCS
  changes, pre-commit and **Format All Files** all leave it alone;
- **suppresses the same formatting-related rules** in the checker, on every surface: the desktop app,
  [`mlqt check`](cli.md) and the [MCP server](mcp-server.md);
- **takes the class off the layout coverage dimensions** on the
  [Coverage dashboard](metrics-dashboard.md), so it is not counted for a gap no finding will name;
- **travels with the class** when it is renamed or moved to another file, because it is part of it.

Give a `reason`. Nothing reads it, and the next person to wonder why this one class looks different
will.

`mlqt check --no-suppress` ignores it, along with every other `__MLQT` directive, so an audit run
still shows what has been waived — including the layout rows on the coverage figures.

The two mechanisms are otherwise interchangeable, and the name list stays supported: a class named in
`FormattingExcludedModels` **or** carrying the annotation is excluded. The list is no longer written
by the toolbar button, and you can still add names to it by hand — for a class whose source you
cannot change, such as one in a reference-only repository.

### Effect on Style Checking

Excluding a model from formatting affects which style findings are reported:

- **Formatting-related style rules are suppressed** for excluded models. These are the rules that correspond to formatting operations (section ordering, import placement, component ordering, etc.), since findings would be unfixable without the formatter
- **Non-formatting style rules still apply** to excluded models. This includes description checks, documentation checks, icon checks, naming convention checks, spell checking, and model reference validation

### When to Use

- When a model has intentional formatting that should not be changed (e.g., carefully aligned equations)
- When adopting MLQT on an existing repository and certain models need to remain unchanged
- When formatting a particular model causes undesirable structural changes

Use the **annotation** for the first of those — a deliberate, permanent decision about one class — and
the **toggle** for the second, where the exclusion is temporary scaffolding you expect to remove.

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
