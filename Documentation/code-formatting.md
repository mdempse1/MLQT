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

**A record keeps its fields in the order they are written**, whatever this option says, and the rule
does not report them. A record's field order is its constructor's signature — `R(2.0)` sets the
first field declared — so sorting it would silently change what every positional call in the
library sets. The same goes for an `operator record`. For the same reason **a record's `extends`
clause stays where it is among its fields** rather than being moved to the top: Modelica places the
inherited fields where the `extends` clause stands, so moving it would reorder the constructor's
inputs too. `MLQT.Style.ExtendsAtTop` does not report it.

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

A full reformat and a version-control operation never run at the same time. While a full reformat runs, the Library Browser's version-control actions and the **Refresh** button are disabled, as they are during an update's own analysis. And a full reformat is not started while a version-control operation or its analysis is running: MLQT says so, keeps the new settings and re-checks the findings against them, and you press **Format All Files** again once the operation has finished.

### On Manual Refresh

When you click the **Refresh** button to process pending file changes from external edits, formatting is applied to the changed files. Each repository's own formatting settings are used, so files from different repositories are formatted with the correct rules.

See [File Monitoring & Refresh](file-monitoring.md) for details on the refresh process.

## One File Per Class

MLQT stores a library the way Modelica's own directory mapping describes it: **a package is a directory**, holding a `package.mo` for the package itself, **one `.mo` file per class inside it**, and a `package.order` naming them in order.

If a library is currently one `.mo` file holding dozens of classes, a full format **expands it**. `Lib.mo` becomes `Lib/package.mo`, `Lib/Resistor.mo`, `Lib/Capacitor.mo` and so on; nested packages become nested directories; and the original single file is deleted once everything in it has been written somewhere else.
From then on MLQT treats the library as the directory `Lib/` — Refresh, Code Review and version-control updates
place the classes in the new files in the library, and the project records the library at its new
path — exactly as it would after reloading the project, which is not needed.
A file with **syntax errors** is not formatted and not restructured: it is left exactly as it is,
with every class it holds, and MLQT lists it when the format finishes. A single-file library whose
file has a syntax error therefore stays one file; a `package.mo` with a syntax error keeps its inline
classes and its `package.order`, while the package's classes in files of their own are formatted as
usual. Fix the errors and run **Format All Files** again to finish the job.
If any file of a library cannot be written, **none of that library's files is deleted**: MLQT warns
that the library could not be formatted completely, and the log names the files. The library may then
define some classes twice — in the old file and in the new one — which is recoverable, where
deleting the only file that still held a class is not.

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

This decides only whether an **inline** class is moved out. A class that is already in a file of its
own stays there, whatever its name collides with: it is written back to that file, not moved into
`package.mo`. And if a full save ever cannot put a class of the library into any file it writes, it
reports the library as not saved and deletes none of its files, so no class's only copy is removed.

Only a **package** is written as a directory. The Modelica specification lets a directory's
`package.mo` define any class (`model Lib` in `Lib/package.mo`, with more classes beside it), and MLQT
loads such a library, but Format All cannot write it back as it is laid out. It leaves the whole
library untouched — nothing written, nothing deleted — and reports which class to declare as a
`package` before it can be formatted. The same applies to a `package.mo` holding a short class
definition (`package Lib = Other;`).

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

The classes are laid out as the rest of MLQT would lay them out. With formatting switched off for
the repository they are moved exactly as written, and so is any class excluded from formatting,
whether by `__MLQT(format=false)` or by the name list. A class of the package that already has a
file of its own is left as it is. The new files keep the encoding and line endings of the file
they came from. If any class cannot be written - a full disk, a name that is not
a legal file name - the split is undone and the single file is kept: it is deleted only once every
class in it has been written somewhere else.

See [settings-reference.md](settings-reference.md#where-the-rules-live) for turning the rule off if
your repository keeps packages single-file on purpose.

A file that version control reports as newly **Added** is never removed by the tidy-up that follows a save, so a class you have created but not yet committed cannot be lost to it.

## File headers and comments outside the class

A comment that sits outside every class in a file — a licence or copyright header above `within`,
a note between `within ...;` and the class, or one after the class's final `end X;` — **belongs to
the class that heads the file**, and every path that writes the file keeps it:

- **Formatting**, incremental and **Format All Files** alike, keeps it at the top (or bottom) of the
  file. Formatting puts each comment on a line of its own and removes the blank lines around them,
  the same way whichever path formats the file, so the two never disagree about it. A class
  excluded from formatting keeps it exactly as it was written.
- **Split into files**, and the one-file-per-class restructure of a full format, put it at the top
  of the new `package.mo` — it headed the package's file, so it heads the package's file still. The
  new per-class files get none.
- The MCP server's edit tools keep it exactly as it was written; renaming a class keeps it, and
  moving a class to another package takes it along: to the top of the class's new file, or, when the
  class is moved into a file another class heads (which has a header of its own), directly above
  the class.

A comment **between two top-level classes** in the same file is the one position that is still
reported as a syntax error, so a file carrying one is not formatted (see
[When Formatting Does NOT Happen](#when-formatting-does-not-happen)). Nothing carries it: a full
format writes each top-level class to a file of its own, so accepting it would mean deleting it
without a word. Moving it into one of the classes, or above the `within` clause, is the way to keep
it and have the file formatted.

## Long argument lists

**An argument list too long for its line is wrapped, an argument a continuation line.** Each
argument that would take the line past the maximum length (100 characters, not counting the line's
indentation) starts a new line a level in. That includes the first: where keeping it after the `(`
would already make the line too long, the continuation starts with it, and the `(` ends the line:

```modelica
  Buildings.Controls.OBC.CDL.Reals.Sources.TimeTable timTabLin(
    smoothness=Buildings.Controls.OBC.CDL.Types.Smoothness.ConstantSegments,
    table=[
      0, 0;
      0.3, 1;
      0.5, 0;
      0.7, 1;
      1, 0]) "Time table with smoothness method of constant segments";
```

Only an argument written on one line is moved this way. One the formatter itself breaks over lines —
a nested modification written an argument a line, or a data table (below) — keeps its layout, and
so do the lists of a graphics annotation and a list inside one already written an argument a line,
which have layouts of their own. A comment after the `(` already ends that line.

Earlier versions measured only the later arguments, so a first argument like the one above stayed
on the declaration's line at 110 characters or more. Over the Modelica Standard Library and
Buildings (8,367 files), 1,017 files are written differently from before, every one only in line
breaks and indentation.

**"A level in" is measured from the line the list opens on**, which is not always the line the
statement starts on. A call that itself starts a continuation line has its arguments a level in
from that line, and so does a graphics element written after `graphics={`:

```modelica
  y = a
    + Some.Package.fn(table=table, iSam=pre(iSam),
      Q_flow=QAve_flow, samplePeriod=samplePeriod);
```

Earlier versions could write such a list's last lines at the statement's continuation level, or
even to the left of the line they continue — an Icon's `color=` two spaces left of the
`graphics={Line(points=...` it belongs to. Over the Modelica Standard Library and Buildings, 150
files are written differently from before, only in indentation.

A nested modification is the same: one that starts a continuation line has its own wrapped
arguments a level in from its name, not at the column of the name or to the left of it:

```modelica
  Modelica.Fluid.Interfaces.FluidPort_a port_1(redeclare package Medium = Medium,
    m_flow(
      min=if (portFlowDirection_1 == PortFlowDirection.Entering) then 0.0 else -Modelica.Constants.inf,
      max=if (portFlowDirection_1 == PortFlowDirection.Leaving) then 0.0 else Modelica.Constants.inf));
```

That changed a further 94 files of the two libraries, again only in indentation.

An argument's own expression, wrapped before a `+` or `-`, continues a level in from the argument
rather than at its column:

```modelica
  state := ThermodynamicState(p=p,
    T=(h - reference_h - (p - reference_p)*((1 - beta_const*reference_T)/reference_d))/cp_const
      + reference_T);
```

Earlier versions wrote `+ reference_T` at the column of `T=`, and an equation's right-hand side
continued after a wrapped `=` two spaces to the left of it. Over the Modelica Standard Library and
Buildings, 19 files are written differently from before, only in indentation and only to the right.

An equation's right-hand side written on a line of its own after a wrapped `=` is continued a level
in from that line, as an unwrapped equation's is from its own:

```modelica
  terminal_n.phase[1].v - terminal_p.phase[1].v
      = productAC1p(Z11, terminal_n.phase[1].i) + productAC1p(Z12, terminal_n.phase[2].i)
        + productAC1p(Z13, terminal_n.phase[3].i);
```

Earlier versions wrote the `+` at the column of the `=`. Over the Modelica Standard Library and
Buildings, 10 files are written differently from before, only in indentation and only to the right.

A term wrapped inside an if-expression or an array is a level further in than the statement's own
continuation, since it continues a branch or an element rather than the whole right-hand side:

```modelica
  w = sum({sTM[j, k].re*v[k].re*aaaaaaaaaaaaaaaaaaaa
      - sTM[j, k].im*v[k].im*bbbbbbbbbbbbbbbbbbbbbbbbb + cccccccccccccccccccc for k in 1:m});
```

A call's parentheses alone do not count, so an `assert` message continues at the statement's column
as before. Earlier versions wrote such a term at the statement's continuation column, where it read
as a term of the whole right-hand side. Over the Modelica Standard Library and Buildings, 54 files
are written differently from before (34 MSL, 20 Buildings), 104 lines each two spaces to the right
and nothing else.

**A long if-expression breaks at its branches, and a long call between its arguments**, before
either is wrapped at a `+` or `-`. An if-expression in an equation or a statement that does not fit
on the line it starts on has each `elseif`, `else if` and `else` start a line of its own, a level
in from the statement's continuation; one that fits stays on its line, and so does one inside
another's condition or `then` branch, whose `else` would read as the outer one's. A call's later
positional argument that does not fit after its `,`, but does on a line of its own, starts one, a
level in from the line the call opened on:

```modelica
  a_relfric/unitAngularAcceleration
      = if locked then 0
          else if free then sa
          else if startForward then sa - tau0_max/unitTorque
          else if startBackward then sa + tau0_max/unitTorque
          else sa - sign(w_relfric)*tau0_max/unitTorque;
  y = Complex(sum({sTM[j, k].re*v[k].re - sTM[j, k].im*v[k].im for k in 1:m}),
    sum({sTM[j, k].re*v[k].im + sTM[j, k].im*v[k].re for k in 1:m}));
  assert(abs(flowCharacteristics.y[size(flowCharacteristics.y, 1)] - 1) < Modelica.Constants.eps,
    "flowCharateristics.y[end] must be 1.");
```

Only where nothing else fits does the formatter wrap at a `+` or `-`: a branch too long for its own
line wraps a level in from its `else`, and an argument too long for a line of its own - an assert
message built from several strings, say - stays after its `,` and wraps as before. A string written
over several lines is left where it is, and so is anything inside a first argument that may yet be
moved to a line of its own, or inside an array still on the line it opened on, which have rules of
their own (above). Earlier versions wrapped both only where the line ran out, mid-term: MSL's
`PartialFriction` had `else if startBackward then sa` ending one line and
`+ tau0_max/unitTorque else if ...` starting the next. Over the Modelica Standard Library and
Buildings, 321 files are written differently from before (141 MSL, 180 Buildings), only in line
breaks and indentation - 114 only in an `assert` whose message now starts a line, 94 only in
if-expressions; lines over 100 characters in those files go from 9,172 to 8,639.

A call's first positional argument is treated the same way: one that does not fit after the `(`,
but does on a line of its own, starts one, and the arguments after it follow it there:

```modelica
  tSho := Buildings.Fluid.Geothermal.Borefields.BaseClasses.HeatTransfer.ThermalResponseFactors.timeGeometric(
    tSho_min, tSho_max, nTimSho);
```

Earlier versions left it after the `(`, at the end of a line already past the limit, and started the
next line with the second argument - `multipoleThermalResistances(2,` then `3, xPip, ...`. Over the
Modelica Standard Library and Buildings, 50 files are written differently from before (12 MSL, 38
Buildings), only in line breaks and indentation.

**A long logical expression wraps before an `or` or `and`**, as an arithmetic one does before a `+`
or `-`: where what the operator joins does not fit after it, but does on a line of its own, the
operator starts a continuation line. That covers an if-expression's condition as well as a Boolean
right-hand side, and the `or` after a term that has wrapped starts a line too, so that what it joins
is not read as part of the `and` before it:

```modelica
  diff = if (time >= t0) and (time < t1) or (time >= t2) and (time < t3)
        or (time >= t4) and (time < t5) then abs(u1 - u2)
      else 0;
  newActive = activeSteps > 0 and not Modelica.Math.BooleanVectors.anyTrue(suspend.reset)
    and not outerState.subgraphStatePort.suspend
    or Modelica.Math.BooleanVectors.anyTrue(resume.set) or outerState.subgraphStatePort.resume;
```

As with a `+`, nothing inside parentheses wraps unless they are too long for a line of their own
(below). Nor does one inside another if-expression's condition or `then`, nor an annotation's, nor a
`for` loop's range. The condition of an `if`, `when` or `while` does, a level past what it guards
(below). Earlier versions never wrapped a logical expression: the condition above was
left whole on one line and the line broke inside `abs(u1 - u2)`. Over the Modelica Standard Library
and Buildings, 31 files are written differently from before (15 MSL, 16 Buildings), only in line
breaks and indentation - most of them an `assert` condition.

**A condition does not wrap straight after a lone flag.** Where a condition's first operand is just
a Boolean name - `tableOnFile`, `not have_chiWat` - the `and` or `or` after it stays on its line, so
the line does not end with nothing of the condition but `if tableOnFile`. The condition wraps at its
next `and` or `or` instead, or, with only two operands, is kept whole:

```modelica
    if tableOnFile then if isCsvExt then "Values" else tableName else "NoName", if tableOnFile and fileName <> "NoName"
        and not Modelica.Utilities.Strings.isEmpty(fileName) then fileName
      else "NoName",
  final parameter Modelica.Units.SI.Time t_in_start=
    if initDelay and (abs(m_flow_start) > 1E-10*m_flow_nominal) then min(
```

Earlier versions wrote `if tableOnFile` at the end of one line and `and fileName <> "NoName" and ...`
at the start of the next (MSL's `Blocks.Tables.CombiTable1Ds`), and `t_in_start=if initDelay` above
`and (abs(m_flow_start) > ...)` (Buildings' `PlugFlowTransportDelay`). Keeping the condition whole
can leave its line past the limit; a binding whose first line would do so starts a line of its own
after its `=` (below), as `t_in_start` does. An `or` whose right-hand side is an
`and` of several still starts a line after the flag, so that the `and` is not read as joining the
`or`. Over the Modelica Standard Library and Buildings, 7 files are written differently from before
(2 MSL, 5 Buildings), only in line breaks and indentation; lines over 100 characters in them go from
371 to 376.

**The argument after an if-expression that has broken its branches starts a line of its own**, so
that it is not read as part of the last branch:

```modelica
  head = homotopy(if s > 0 then (N/N_nominal)^2*flowCharacteristic(V_flow_single*N_nominal/N)
      else (N/N_nominal)^2*flowCharacteristic(0) - s*unitHead,
    N/N_nominal*flowCharacteristic(0) - s*unitHead);
```

Earlier versions wrote it after the `else` branch, on its line. Over the Modelica Standard Library
and Buildings, 3 files are written differently from before, only in line breaks and indentation.

**Whether a named argument in an equation or statement fits is judged as it will be written**,
spaces included, as a positional argument's is, so one that does not fit starts a line rather than
being wrapped inside:

```modelica
  z := Buildings.Utilities.Math.Functions.cubicHermiteLinearExtrapolation(x=u, x1=xd[i],
    x2=xd[i + 1], y1=yd[i], y2=yd[i + 1], y1d=d[i], y2d=d[i + 1]);
```

Earlier versions judged it from its text without spaces, which is shorter, and wrote `x2=xd[i` at
the end of the line and `+ 1], y1=...` at the start of the next. A declaration's modifications are
judged as before. Over the Modelica Standard Library and Buildings, 8 files are written differently
from before (2 MSL, 6 Buildings), only in line breaks and indentation.

**Nothing inside a subscript is wrapped**, as nothing inside a matrix's brackets is: a subscript's `+` or
`-` stays with the index it is part of, and the statement wraps elsewhere or not at all:

```modelica
  kOpa[i + nConExt + 2*nConPar] = Modelica.Constants.sigma*epsConBou[i]*AOpa[i + nConExt + 2*nConPar];
```

Earlier versions wrote `AOpa[i + nConExt` at the end of the line and `+ 2*nConPar];` at the start
of the next. Over the Modelica Standard Library and Buildings, 4 files are written differently from
before (1 MSL, 3 Buildings), only in line breaks and indentation. In one of them, MSL's
`Fluid.Pipes.BaseClasses.FlowModels.PartialGenericPipeFlow`, the break inside a subscript was the
only thing that split a long first argument - `actual=WallFriction.massFlowRate_dp_staticHead(...)`
- over lines; without it the argument is moved whole to a line of its own, which it does not fit.

**The condition of an `if`, `elseif`, `when`, `elsewhen` or `while` wraps as an equation does**,
before an `and`, `or`, `+` or `-`, in every branch, with its continuation lines a level past the
equations or statements it guards, so they are not read as one of them:

```modelica
  if not ATotExt > 0 and not ATotWin > 0 and not AInt > 0 and AFloor > 0 then
    connect(thermSplitterIntGains.portOut[1], floorRC.port_a);
  elseif ATotExt > 0 and not ATotWin > 0 and not AInt > 0 and AFloor > 0
      or not ATotExt > 0 and ATotWin > 0 and not AInt > 0 and AFloor > 0
      or not ATotExt > 0 and not ATotWin > 0 and AInt > 0 and AFloor > 0 then
    connect(thermSplitterIntGains.portOut[2], floorRC.port_a);
```

Earlier versions wrapped only the first branch's condition, only at a `+` or `-` or between a call's
arguments, and at the column of its body; an `elseif`'s was never wrapped, nor was any condition at
an `and` or `or`. A condition that is a single comparison still stays on its line. Over the
Modelica Standard Library and Buildings, 40 files are written
differently from before (10 MSL, 30 Buildings), only in line breaks and indentation; lines over 100
characters in them go from 2,722 to 2,620, and conditions over 100 characters from 145 to 39.

**A component's binding wraps as an equation's right-hand side does** - before a `+`, `-`, `and`
or `or`, at an if-expression's branches, and between a call's arguments - and its description
follows the last line:

```modelica
  parameter SI.Voltage ViNominal=VaNominal
    - Machines.Thermal.convertResistance(Ra, TaRef, alpha20a, TaNominal)*IaNominal
    - Machines.Losses.DCMachines.brushVoltageDrop(brushParameters, IaNominal)
    "Induced voltage at nominal operating point";
  parameter Modelica.Units.SI.MassFlowRate m_flow_nominal=m0_flow_cor + m0_flow_sou + m0_flow_eas
    + m0_flow_nor + m0_flow_wes "Nominal air mass flow rate";
```

Earlier versions never wrapped a declaration's expression at an operator, so a long binding stayed
on the declaration's line, or wrapped only where it held a list of named arguments. Only the binding
after the component's own `=`: a modification's value - `x(start=...)` - is laid out as before.
Over the Modelica Standard Library and Buildings, 371 files are written differently from before (93
MSL, 278 Buildings), only in line breaks and indentation; lines over 100 characters in them go from
9,263 to 8,816.

**A binding whose first line would pass the limit starts a line of its own** after the `=`, a level
in, and wraps from there as above; its description follows its last line:

```modelica
  parameter Modelica.Units.SI.SpecificHeatCapacity cpSou_default=
    if typ == Buildings.Templates.Components.Types.HeatPump.AirToWater then Buildings.Utilities.Psychrometrics.Constants.cpAir
        else Buildings.Utilities.Psychrometrics.Constants.cpWatLiq
    "Source fluid default specific heat capacity"
  parameter Modelica.Units.SI.SpecificHeatCapacity cpHea_default=
    MediumHea.specificHeatCapacityCp(MediumHea.setState_pTX(MediumHea.p_default, MediumHea.T_default,
      MediumHea.X_default)) "Specific heat capacity of heating medium at default medium state";
```

The binding is judged by where its first line would end on the declaration's line: one that wraps
within the limit there - `ViNominal=VaNominal` above - stays. One that is moved is laid out from its
new line as a statement starting that line would be, so an `else` is two levels past its `if`, as
in an equation, and whatever wraps inside an argument list moves in with the arguments. A short binding is moved too when it would take the line past the limit
(`tit24CliZon=` / `datAll.tit24CliZon "California Title 24 climate zone"`), but not when the line
is 20 characters or fewer up to the `=` - `SI.Length h=`, where moving gains too little, as an
equation's left-hand side of that length does not wrap at its `=` - nor when the line is past the
limit before the `=`: there the modification is what is too long. A modification's value -
`x(start=...)` - is not moved. Moving cannot shorten what cannot break: `cpSou_default`'s
`if ... then ...cpAir` above is still 118 characters. Earlier versions kept every binding on the
declaration's line, so `cpSou_default=if ...` was 152 characters (Buildings'
`Templates.Components.Data.HeatPump`). Over the Modelica Standard Library and Buildings, 686 files
are written differently from before (129 MSL, 557 Buildings), only in line breaks and indentation;
lines over 100 characters in them go from 12,603 to 11,268.

**An expression in parentheses too long for a line of its own wraps inside them**, as it would
outside them, with its continuation lines a level in from the line the `(` is on. A parenthesised
if-expression breaks at its own branches, as one standing alone does:

```modelica
  startForward = pre(mode) == Stuck and (sa > f0_max/unitForce and s < (smax - L/2)
      or pre(startForward) and sa > f0/unitForce and s < (smax - L/2));
  mode = if (pre(mode) == Backward or startBackward) and v_relfric > 0 then Forward
      else (if (pre(mode) == Forward or pre(mode) == Free or startForward) and v_relfric > 0
          and s < (smax - L/2) then Forward
        else if (pre(mode) == Backward or pre(mode) == Free or startBackward) and v_relfric < 0
          and s > (smin + L/2) then Backward
        else Stuck);
```

Parentheses that would fit on a line of their own are left whole, for whatever is outside them to
wrap before, and nothing inside a matrix's brackets, a subscript or an annotation wraps. A
polynomial written in nested form is written flat (below).
Earlier versions never wrapped inside parentheses, so MSL's `MassWithStopAndFriction` had an
`else (if ... )` of 300 characters, and `Media.Water.IF97_Utilities` lines of over 3,000. Over the
Modelica Standard Library and Buildings, 103 files are written differently from before (57 MSL, 46
Buildings), only in line breaks and indentation; their lines longer than 100 characters, not counting
indentation, go from 4,689 to 4,553, and the text past the hundredth column falls by 9%. Counting
indentation, lines over 100 characters rise from 5,855 to 6,006: a nested polynomial's inner steps
are indented past the column where their text would have started.

**A polynomial written in nested form is written a link a line, at one column** - `a*(b + x*(c +
x*(d + ...)))`, where each pair of parentheses holds a term and a `+` before the next. The closing
parentheses end the last line:

```modelica
    h := 639675.036*(0.173379420894777
        + pi1*(-0.022914084306349
        + pi1*(-0.00017146768241932
        + pi1*(-4.18695814670391e-6
        ...
        + o[1]*o[2]*(-1.43870236842915e-44
        + pi1*(1.73894459122923e-45 + (-7.06381628462585e-47 + 9.64504638626269e-49*pi1)*pi1)))))))))));
```

Only a chain of such links is written flat: each holds two terms, the first short enough for a line
of its own, and ends the parentheses around it after a `+` and a coefficient of names and numbers,
and there are at least two in a row. Anything else in parentheses - a sum of more terms, a term after
a `-` or a parenthesised factor, one raised to a power - still steps a level in, so that its terms are
not read as the ones around it, and a chain continues flat after it. Earlier versions stepped a level
in at each `(`, so MSL's `Media.Water.IF97_Utilities.BaseIF97.Regions.hlowerofp1` was a staircase
eleven levels deep. Over the Modelica Standard Library and Buildings, 4 files are written differently
from before (2 MSL, 2 Buildings), only in indentation; lines over 100 characters in them go from 638
to 616.

**The arguments of a wrapped list start at one column**, whether or not an argument's own list
wraps in turn. Earlier versions wrote an argument whose own list wrapped a level deeper than its
siblings - `nomVal=` two spaces right of `spe=` and `perCur=`:

```modelica
  parameter Data.DXCoil datCoi(sta={Some.Long.Package.Stage(
    spe=900/60,
    nomVal=Some.Long.Package.NominalValues(
      Q_flow_nominal=-12000, COP_nominal=3, SHR_nominal=0.8),
    perCur=Some.Long.Package.Curve_I())}, nSta=1);
```

Over the Modelica Standard Library and Buildings, 145 files are written differently from before,
only in indentation and only to the left.

The same holds in a list written an argument a line whatever its length, such as a graphics
element's: an argument that is also too long for its line - a long `textString=` - is at the column
of its siblings. Earlier versions wrote it two spaces further right; 4 files of the Modelica
Standard Library change, only in indentation and only to the left.

**An array of calls that has already wrapped starts each call that would not fit on a line of its
own**, a level in from the line the array opens on - or, inside a list written an argument a line,
at the column the call before it ended at. Earlier versions kept each call on the last line of the
one before it, so each call's wrapped arguments were a level deeper than the last one's, and those
lines ran to 170 characters:

```modelica
  parameter Data.DXCoil datCoi(sta={Some.Long.Package.Stage(
    spe=900/60,
    nomVal=Some.Long.Package.NominalValues(
      Q_flow_nominal=-12000, COP_nominal=3),
    perCur=Some.Long.Package.Curve_I()),
    Some.Long.Package.Stage(spe=1200/60,
      nomVal=Some.Long.Package.NominalValues(
        Q_flow_nominal=-18000, COP_nominal=3),
      perCur=Some.Long.Package.Curve_I())}, nSta=2);
```

Only calls are moved; an array of numbers is not wrapped for length at all. A graphics annotation's
arrays keep their own layout, an element a line. Over the Modelica Standard Library and Buildings,
63 files are written differently from before, only in line breaks and indentation.

**An array of calls still on the line it opened on is wrapped the same way.** When it is in the first
argument of a list, that argument is first moved to a line of its own, as a first argument too long
for the opening line always is, and the array wraps from there only if it still does not fit. Its
wrapped calls are a level in from the line the array opened on, in a list written an argument a line
too:

```modelica
record ASHRAE_901_1975Roof = Buildings.HeatTransfer.Data.OpaqueConstructions.Generic(
  final material={Buildings.HeatTransfer.Data.Solids.GypsumBoard(x=0.016),
    Buildings.HeatTransfer.Data.Solids.InsulationBoard(x=0.09652),
    Buildings.HeatTransfer.Data.Solids.Plywood(x=0.0127)}, final nLay=3) "...";
```

Earlier versions left such an array on its line, up to 600 characters long in Buildings' FLEXLAB
constructions and over 1,000 in its IEEE 34-bus grid, and at about 200 in MSL's
`Fluid.Examples.PumpingSystem` and `MultiBody.Frames.Orientation`. Over the Modelica Standard Library and Buildings, 57 files are written
differently from before (9 MSL, 48 Buildings), only in line breaks and indentation, and lines over 100
characters in them fall from 640 to 599.

The same holds for an array in a call's later positional argument. Positional arguments are not
wrapped for length, so such an array used to break after its first element; the argument now moves
to a line of its own first:

```modelica
  R_rel = Frames.axesRotations(sequence_start, {angle[1], angle[2], angle[3]},
    {der(angle[1]), der(angle[2]), der(angle[3])});
```

Over the Modelica Standard Library and Buildings, 2 files are written differently from before -
MSL's `MultiBody.Joints.Internal.InitAngle` and Buildings' EnergyPlus `RoomModel` - only in line
breaks and indentation.

An array with no argument to move - one after a `=` in an argument that already starts its line, or
in a declaration - whose line is already past the limit when a call in it has to wrap moves to a
line of its own after the `=`, provided what it has written then fits:

```modelica
    redeclare Buildings.Electrical.Transmission.LowVoltageCables.Generic cables=
      {LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl120(),
        LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl70(), LowVoltageCables.PvcAl35()}
```

Over the Modelica Standard Library and Buildings, 2 files are written differently from before -
Buildings' `Electrical.Transmission.Grids.IEEE_34_AL120` and
`Fluid.Actuators.BaseClasses.PartialDamperExponential` - only in line breaks and indentation, each
with one line over 100 characters fewer.

**A description too long for its line starts a line of its own**, a level in, for a short class
definition as for a component:

```modelica
record Construction2 = Buildings.HeatTransfer.Data.OpaqueConstructions.Generic(
  final material={Buildings.HeatTransfer.Data.Solids.InsulationBoard(x=0.08255),
    Buildings.HeatTransfer.Data.Solids.Plywood(x=0.0127),
    Buildings.HeatTransfer.Data.Solids.GypsumBoard(x=0.01588)}, final nLay=3)
  "South wall in test bed X2"
```

Earlier versions moved a component's description and never a short class's, so once an array like
the one above wrapped, its closing argument joined the short last line and the description took it
past the limit. An enumeration's description stays after its `)`, which starts a line of its own
already. Over the Modelica Standard Library and Buildings, 101 files are written differently from
before (25 MSL, 76 Buildings), only in line breaks and indentation, and lines over 100 characters in
them fall from 2,569 to 2,429 - each one left over is part of a longer line it was split from, most
of them a description longer than the limit on its own.

Whether a description fits is judged by **the line as it will be written**: its indentation - a
`protected` section's included - and the `;` after the description (an annotation after it starts a
line of its own, so there is none to count). A declaration nested a few classes down, or the last
line of a binding moved to a line of its own, no longer ends past the limit because of its
description:

```modelica
  parameter Modelica.Units.SI.SpecificHeatCapacity cpDom_default=
    MediumDom.specificHeatCapacityCp(MediumDom.setState_pTX(MediumDom.p_default, MediumDom.T_default,
      MediumDom.X_default))
    "Specific heat capacity of domestic hot water medium at default medium state";
```

Earlier versions measured the line without its indentation or its `;`, so Buildings'
`DHC.Loads.HotWater.StorageTankWithExternalHeatExchanger` ended `MediumDom.X_default)) "Specific
heat capacity ..."` at 106 characters, and MSL's
`Fluid.Dissipation.HeatTransfer.General.kc_approxForcedConvection`, eight spaces in, had
`SI.Diameter d_hyd=... "Hydraulic diameter";` at 102. Only this decision counts the indentation:
where an expression wraps is still judged without it. Over the Modelica Standard Library and
Buildings (8,899 files), 972 files are written differently from before (209 MSL, 763 Buildings),
only in line breaks and indentation; lines over 100 characters in them go from 13,804 to 11,980.

**Graphics are laid out the same wherever the annotation is.** A class's `Icon` and `Diagram`
graphics are written an element a line, with each element's arguments a line each a level in. The
annotation of a short class definition - `connector RealInput = input Real annotation (...)` - and a
component's annotation whose `Icon` or `Diagram` is written an argument a line now get the same
layout:

```modelica
connector RealInput = input Real "'input Real' as connector"
  annotation (
    defaultComponentName="u",
    Icon(
      graphics={
        Polygon(
          lineColor={0, 0, 127},
          fillColor={0, 0, 127},
          fillPattern=FillPattern.Solid,
          points={{-100.0, 100.0}, {100.0, 0.0}, {-100.0, -100.0}}
        )
      },
      ...
```

Earlier versions wrote `fillColor=` and the arguments after it at the column of
`graphics={Polygon(`. An `Icon(graphics={...})` written on one line keeps its graphics wrapped for
length, as before. Over the Modelica Standard Library and Buildings, 22 files are written
differently from before, only in line breaks and indentation - among them MSL's `Blocks.Interfaces`
and Buildings' CDL connectors.

### Layouts the formatter leaves as they are

The rules above fix layouts that are clearly wrong: a line the formatter takes past the limit when a
break could have kept it inside, or a wrap that makes code read as something it is not. A few shapes
remain where engineers would disagree about the better layout, and these are left as they are on
purpose. Each one names a class where you can see it:

- **A first argument too long for any line is moved whole, never wrapped inside.** MSL's
  `Fluid.Pipes.BaseClasses.FlowModels.PartialGenericPipeFlow` has
  `actual=WallFriction.massFlowRate_dp_staticHead(...)` on a line of about 330 characters, and
  Buildings' `Fluid.DXSystems.Cooling.BaseClasses.Evaporation` has `pos=if ...` at 122.
- **A `+` or `-` in an array comprehension's `for` range can still wrap.** Buildings'
  `Controls.Predictors.ElectricalLoad` ends a line with `for i in iDayOf_start:iDayOf_end` and
  starts the next with `- 1}) then`.
- **An if-expression's `then` value never starts a line of its own.** A long `if cond then value`
  therefore stays whole. Buildings' `Templates.Components.Data.HeatPump` `cpSou_default` is 118
  characters, and in `Fluid.Movers.BaseClasses.FlowMachineInterface`, `etaDer=` ends its line in
  `zeros(size(`.
- **A call's first argument, moved to a line of its own after `then`, can sit left of the `else`
  below it.** For example, Buildings' `Fluid.FixedResistances.BaseClasses.PlugFlowTransportDelay`
  (`then min(` / `length/m_flow_start*..., 0)` / `else 0`).
- **A binding that is a call, moved after its `=`, can leave a line holding only the call's name and
  `(`.** For example, MSL's `Blocks.Sources.CombiTimeTable` (`tableID=` /
  `Modelica.Blocks.Types.ExternalCombiTimeTable(` / its arguments), and the `CombiTable1D*` and
  `CombiTable2D*` tables.
- **An `or` after a lone flag and before an `and` group keeps the flag at the end of its line**
  (`if have_pumChiWatPriDed` / `or have_chiWat and ...`, in Buildings'
  `Templates.Plants.HeatPumps.Interfaces.PartialHeatPumpPlant`). The only other break is before the
  `and`, and that would make the code look as if `and` binds more loosely than `or`.
- **A nested polynomial whose links alternate between two and three terms is only partly flat.** It
  steps in at each three-term link and is flat between them (MSL's
  `Media.Water.IF97_Utilities.BaseIF97.Basic.g2`, Buildings' `Media.Steam.g2`).

If one of these matters in a class of yours, keep your own layout with `__MLQT(format=false)` (see
[Excluding Models from Formatting](#excluding-models-from-formatting)).

## Section keywords

`public`, `protected`, `equation`, `algorithm` and `external` are written at the column of the class
they belong to - the column of its `model` (or `function`, `package`, ...) and `end` lines - with the
section's contents a level in. A top-level class's are at column 0; a class nested inside another in
the same file has them at its own indentation:

```modelica
      function kc_evenGapLaminar "Mean heat transfer coefficient of even gap | laminar flow regime | ..."
        ...
        output Real failureStatus
          "0== boundary conditions fulfilled | 1== failure >> check if still meaningful results"
          annotation (Dialog(group="Output"));
      protected
        type TYP = Modelica.Fluid.Dissipation.Utilities.Types.kc_evenGap;
```

Earlier versions wrote a nested class's `public` and `protected` at column 0, under correctly
indented elements - MSL's `Modelica.Fluid.Dissipation.HeatTransfer.Channel.kc_evenGapLaminar` above
among 79 in that file. Over the Modelica Standard Library and Buildings (8,899 files), 75 files are
written differently from before (68 MSL, 7 Buildings), only in indentation, and 978 `public` and
`protected` lines move to their class's column.

## Matrices and data tables

**A matrix keeps its rows where you wrote them.** Where a row starts a new line in the source, it
starts a new line when the file is formatted, one level in from the line the matrix begins on; rows
you wrote on one line stay on one line, and a table written entirely on one line stays that way:

```modelica
  Modelica.Blocks.Sources.TimeTable setPoint(table=[0, 20;
    8*3600, 21;
    18*3600, 20;
    24*3600, 20]) "Set point schedule";
  parameter Real k[:, :]=[1, 2; 3, 4];
```

The first row is no different: written on the line after the `[`, it stays there, a level in like
the rows after it; written on the `[` line, it stays on that one:

```modelica
  Modelica.Blocks.Sources.TimeTable setPoint(table=[
    0, 20;
    8*3600, 21;
    24*3600, 20]) "Set point schedule";
```

Only where the rows break is kept, not how far they are indented, and a line break *inside* a row
(between two of its elements) is not kept — the elements of a row are written on one line. A comment
after a row's `;` stays where it was, and the next row starts a line of its own after it.

Earlier versions wrote every matrix on one line, so a table laid out a row a line came back as a
single line that could run to thousands of characters. Over the Modelica Standard Library and
Buildings (8,367 files), 232 files are now written differently from before, and in every one of them
the only difference is line breaks and indentation inside a matrix. A file an earlier version has
already saved has lost its rows — there is nothing left in it to say where they were — so it keeps
its one-line tables until you break them by hand; from then on they stay broken. Keeping a first
row below its `[` came later and changed 86 more files the same way, every one only in line breaks
and indentation.

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
- **It reverts only when formatting is all the revert would take back**: the file must be modified (not new), and its content must be exactly what formatting the committed version produces. A file that has never been committed is not reverted — a revert would delete it — and nor is one holding any other uncommitted edit, such as a hand change to another class in the same `package.mo`. In those cases the annotation is still written, the formatting already applied stays, and a message says why the file was left alone
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
- **Files with syntax errors** — A file that does not parse is left exactly as it is, by every formatting path. The formatter can still produce output for malformed Modelica, but that output is not a faithful copy of the file, so writing it back could lose code. The incremental path notes the file in the log; **Format All Files** also tells you which files it left alone when it finishes. Code Review lists the syntax errors (`MLQT.Parse.SyntaxError`); fix them and the file is formatted the next time.
- **Files not in the graph** — If a file has not been loaded into the library graph (e.g., a newly added file that hasn't been refreshed), it cannot be formatted by the incremental formatter. Use the Refresh button to load new files first.

## File Monitor Coordination

During formatting operations, the file monitor is temporarily paused to prevent MLQT's own file writes from being detected as external changes. The sequence is:

1. Pause the file monitor — for every repository in the same working copy, not only the one being formatted, so a second library checked out in that tree does not record the formatter's writes as its own changes
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

1. **Pre-parse** — Every file of the library is checked for syntax errors, and one that has any is set aside untouched with the classes it holds; all the other models are parsed in parallel
2. **Structure build** — The parent-child package tree is constructed
3. **Pre-render** — All models are rendered in parallel with the new formatting rules
4. **Write** — Files are written sequentially, and orphaned files (no longer needed) are cleaned up

Full formatting can also reorganize the file structure — for example, moving a model that was previously nested in a `package.mo` file into its own standalone file, or vice versa.
