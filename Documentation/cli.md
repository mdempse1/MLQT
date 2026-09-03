# MLQT CLI (`mlqt`)

A headless, cross-platform command-line tool that style-checks a Modelica library and reports
findings. It runs the same checks as the MLQT desktop app, with no UI and no MAUI dependency, so it
works on Windows, Linux, and macOS and is suited to CI pipelines.

> For a step-by-step guide to setting up the CI quality gate on a real library (enable rules,
> baseline existing debt, gate on new findings, wire into TeamCity/GitHub), see
> [ci-quality-gate.md](ci-quality-gate.md). This page is the option reference.

## Install

Packaged as a .NET tool:

```bash
dotnet tool install --global MLQT.Cli      # provides the `mlqt` command
# or into an isolated location:
dotnet tool install --tool-path ./tools MLQT.Cli
```

Requires the .NET 10 runtime.

## Usage

```
mlqt check <library-path> [options]
```

`<library-path>` is a Modelica library directory (a package with a `package.mo`, or a flat folder
of `.mo` files) or a single `.mo` file.

There are three other commands: `mlqt baseline` ([below](#baseline--ratchet)) manages the
accepted-debt file, `mlqt compare` ([below](#comparing-two-copies-of-a-library)) lists the classes one
copy of a library has that another does not, and `mlqt hook`
([below](#running-the-check-before-each-commit)) installs the check as a git pre-commit hook.

| Option | Description | Default |
|--------|-------------|---------|
| `--config <path>` | Settings file to use | the nearest `.mlqt/settings.json` at or above the library, else built-in defaults |
| `--baseline <path>` | Classify findings against a baseline (new vs accepted debt) | none |
| `--changed-from <ref>` | VCS ref to diff against, to escalate debt in changed models | none |
| `--touched-debt warn\|fail\|ignore` | Existing debt in a model the change touched: report it, gate on it, or leave it out of the report entirely | `warn` |
| `--format console\|json\|junit\|sarif\|teamcity\|markdown` | Output format ([details](#output-formats)) | `console` |
| `--sarif-base <path>` | Directory the file paths in SARIF output are written relative to. Set it to the repository root when the library is a subdirectory | the library |
| `--sarif-include-accepted` | Keep accepted debt in SARIF output. Off by default — see [SARIF and GitHub](#sarif-and-github) | off |
| `--out <file>` | Write the primary output to a file instead of stdout | stdout |
| `--report <fmt>:<file>` | Also write this format to this file. Repeatable — see [Several reports from one run](#several-reports-from-one-run) | none |
| `--fail-on off\|warning\|error` | Exit non-zero when findings reach this level | `error` |
| `--min-coverage <spec>` | Fail when coverage is below a percentage — see [Gating on coverage](#gating-on-coverage). Repeatable | none |
| `--coverage-ratchet` | Fail when any dimension is below the last recorded snapshot | off |
| `--no-color` | Disable coloured console output (also honours `NO_COLOR`) | colour on a TTY |
| `--dependency <path>` | Load another library so references resolve; never reported on. Repeatable | none |
| `--allow-version-mismatch` | Continue despite a dependency version mismatch (findings may not be real) | off |
| `--metrics` | Record a coverage snapshot in `<library-path>/.mlqt/metrics-history.json` | off |
| `--metrics-out <path>` | Record it somewhere else instead (implies `--metrics`) | — |
| `--metrics-force` | Record even when the numbers are unchanged (implies `--metrics`) | off |
| `-h`, `--help` | Show help | |

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | No findings at or above `--fail-on` |
| `1` | Findings at or above `--fail-on` |
| `2` | Usage, load or setup error (bad path, unreadable config, dependency version mismatch) |

Because the built-in **style** rules report at **warning** severity, the default `--fail-on error`
is effectively report-only for them (it surfaces findings but exits `0`). Use `--fail-on warning` for
a strict gate, or `--fail-on off` to never fail. **Parse diagnostics are the exception** — they are
errors and fail even the default gate; see below.

## Resolving references into other libraries

`mlqt check <path>` loads only what is under that path. A reference into a library it has not loaded
cannot resolve, and several rules then report findings the code did not earn — most visibly
`MLQT.Doc.ClassIcon`, which cannot see that an icon is inherited from `Modelica.Icons.*`, and
`MLQT.Reference.ModelReferences`, which cannot resolve a `modelica://` link into MSL.

`--dependency <path>` loads a library for resolution only. It is never reported on — you want MSL's
classes visible, not MSL's findings:

```bash
mlqt check ./ExternData --dependency /path/to/ModelicaStandardLibrary
```

The flag is repeatable, and each path is discovered the same way as the positional argument, so
pointing it at an MSL checkout picks up `Modelica`, `ModelicaServices` and the rest in one go. The run
says what it loaded:

```
note: loaded Modelica, ModelicaReference, ModelicaServices, ModelicaTest, … for reference resolution
      (not reported on)
```

On ExternData this removes 96 findings — 91 inherited icons and all 5 `modelica://` references — which
is the same effect as adding MSL to the project in the desktop app.

### Encrypted libraries

`--dependency` also accepts a commercial library that ships **encrypted** — a directory holding a
`package.moe` with no readable source. MLQT recovers its classes from the vendor's generated `help/`
documentation, which is enough for references into it to resolve and for icons inherited from it to
be seen:

```bash
mlqt check ./MyLibrary --dependency "C:\Program Files\Dymola 2026x Refresh 1\Modelica\Library\Battery 2.9.0"
```

```
note: loaded Battery for reference resolution (not reported on)
```

An encrypted library found *inside* the checked path is loaded the same way and likewise never
reported on. A library shipping no documentation is called out rather than passed over silently:

```
warning: encrypted library 'CATIAMultiBody' ships no usable documentation, so its classes cannot be
         recovered; references into it stay unresolved
```

See [encrypted-libraries.md](encrypted-libraries.md) for what is and is not recovered.

### Version mismatches stop the run

The versions a library declares in its `uses(...)` annotation are compared against the copies actually
loaded. A disagreement **stops the check** before any findings are reported:

```
$ mlqt check ./ExternData --dependency /path/to/msl-4.x
error: dependency version mismatch
       ExternData declares Modelica 3.2.2, but 4.2.0 dev is loaded
       Checking against the wrong version reports findings that are not real, so this check has
       been stopped.
       Point --dependency at the declared versions, or update the uses(...) annotation to match
       what you have.
       If the difference is deliberate (a conversion(noneFromVersion=...) covers it, say), pass
       --allow-version-mismatch.
                                                                                       (exit 2)
```

References still *resolve* against the wrong version, but against classes that may have moved, been
renamed or changed signature — so the run would report a pile of findings that are not real. Handing
those back is worse than refusing: someone would spend a morning on them.

The exit code is **2 (setup error)**, not 1 (gate failed). In CI those mean different things: fix your
invocation versus fix your code. `baseline create`/`update`/`prune` refuse for the same reason — a
baseline taken against the wrong versions bakes unreal findings into the ledger, where they are far
harder to notice.

`--allow-version-mismatch` downgrades it to a warning and continues. It exists because a
`conversion(noneFromVersion=...)` annotation can make a version difference legitimate and MLQT does not
read those; the run then says plainly that its findings may not be real.

Comparison is by version segment, and a shorter declaration matches a longer version: `4.0` covers
`4.0.0`, and a build suffix on the loaded copy (`4.2.0 dev`) is not treated as a disagreement with a
declared `4.2.0`. A declared dependency that is not loaded at all is not reported here — that shows up
as unresolved references instead.

**Pass the same set to `baseline` as to `check`.** A baseline generated with MSL loaded and then
checked without it sees a pile of findings the change did not cause. The baseline records the
dependency library *names* (not paths, which differ between a laptop and a CI agent) and the check
warns:

```
warning: the baseline was generated with a different configuration
         not loaded this time: Modelica, ModelicaServices — references into them will not resolve
         Pass --dependency <path> for each, or references into them resolve as findings that the
         change did not cause.
```

## Parse diagnostics

Two findings are reported that are not style rules:

| Rule id | Meaning |
|---------|---------|
| `MLQT.Parse.SyntaxError` | The file has a syntax error. The parser recovered, so the class still loaded — but part of it was misread. |
| `MLQT.Parse.Failure` | The file could not be parsed at all. No classes were extracted from it and nothing in it was checked. |

They behave differently from style rules on purpose, because a file MLQT cannot read is not a matter
of taste and every other rule silently under-reports on it:

- **Always reported.** They ignore the settings file — there is nothing to enable, and they are
  produced even when no style rules are configured at all.
- **Always errors.** They fail the default `--fail-on error` gate.
- **Cannot be suppressed** with a `__MLQT` annotation, and `baseline create` will not record them, so
  a baseline can never accept one.

The same diagnostics appear in the desktop app's Findings panel and from the MCP server's `check_class`
/ `check_library`, with identical wording and line numbers.

### `MLQT.Check.Failed`

One more finding is not a style rule and not a judgement on your code: `MLQT.Check.Failed` says
checking a class threw, so **that class's findings are missing from the results**. It names the class
and the error. It means a defect in MLQT or a setting that cannot be evaluated (a naming pattern that
never terminates, say) — not a problem with the Modelica.

It exists because the alternative is worse: a class that cannot be checked used to be dropped in
silence, so a run's totals could move between two runs over the same code with nothing to explain the
difference, and a clean class looked exactly like one that was never checked. Seeing this finding
means the reported totals are incomplete; please report it.

## Settings

The rules that run are controlled by a `StyleCheckingSettings` JSON file — the same format the
desktop app writes to `<repo>/.mlqt/settings.json`. If no config is found, no style rules are enabled
and only parse diagnostics are produced. See [settings-reference.md](settings-reference.md).

**Where they are found.** Without `--config`, `mlqt` looks for `.mlqt/settings.json` in the library
directory and then in each directory above it, stopping at a working-copy root (one holding `.git` or
`.svn`) so a checkout never picks up settings from outside it. This matches the desktop app: settings
belong to a repository, and a repository usually holds several libraries under one `.mlqt`. So
`mlqt check MyRepo/MyLibrary` uses `MyRepo/.mlqt/settings.json` — the same rules, and the same
accepted spellings, your team sees in the app.

**Per-rule severity.** Enabled rules default to `Warning`. To make a rule fail the gate at
`--fail-on error`, set it to `Error` in a `RuleSeverities` map (keyed by rule id):

```json
{
  "ClassHasDescription": true,
  "ParameterHasDescription": true,
  "RuleSeverities": { "MLQT.Doc.ClassDescription": "Error" }
}
```

Values are `Off`, `Info`, `Warning`, or `Error`. The map wins over the on/off booleans.

### Accepted spellings

When spell checking is enabled, the words your library uses that no dictionary knows are read from
`dictionary.txt` in the `.mlqt` directory the **settings** came from, if there is one there —
otherwise from the nearest `.mlqt/dictionary.txt` at or above the library, found by the same upward
walk as the settings and stopping at the same working-copy root. It is the same file the desktop app
writes when someone chooses **Add to Dictionary**. One word per line; blank lines and `#` comments are
ignored. Commit it, and CI accepts exactly the words your team accepted.

Settings and words usually sit in the same `.mlqt`, and then there is nothing to choose between: a
repository whose settings cover several libraries keeps one word list for all of them. But they are
separate files, and the words are found whether or not the settings were — a shared rules file passed
with `--config`, or no settings file at all, still checks your library against your repository's own
vocabulary.

**Every run says which list it used**, so a job that reports words you know are accepted tells you why
in its own output rather than leaving you to guess:

```
note: 214 accepted spellings from /build/MyRepo/.mlqt/dictionary.txt
```

or, when there is none:

```
note: no accepted spellings; there is no /build/MyRepo/MyLibrary/.mlqt/dictionary.txt
```

If that path is not the one you expected — a checkout that dropped the file, or a `.mlqt` above the
working-copy root the walk stops at — that line is the whole diagnosis.

The language dictionaries themselves are not in the repository. If the settings ask for a language the
build agent has no dictionary installed for, the words are checked against the remaining languages and
`mlqt` writes a warning to stderr, because the run will then disagree with a machine that has it:

```
warning: no spell-check dictionary installed for de_DE; those words are checked against the
remaining languages, so the spelling findings will not match a machine that has them
```

en_US and en_GB ship with the tool. Other languages need their Hunspell `.aff`/`.dic` pair installed
on the agent, under `%LocalAppData%/MLQT/Dictionaries/` (or the equivalent user profile path on Linux
and macOS).

## Baseline / ratchet

A large existing library usually has many findings that no one will fix all at once. A **baseline**
records the current findings as *accepted debt* so CI fails only on **new** findings.

```bash
mlqt baseline create <library-path>     # snapshot current findings -> <library-path>/.mlqt/baseline.json
mlqt baseline prune  <library-path>     # drop entries whose findings are now fixed
mlqt baseline update <library-path>     # regenerate: drop fixed entries AND accept new ones as debt
```

### Entries vs findings

**A baseline holds entries, and a check reports findings. The two counts are not the same number**,
which is why every command prints both:

```
$ mlqt check ./MyLibrary
104447 finding(s): 2840 error(s), 101607 warning(s), 0 info across 38112 model(s).

$ mlqt baseline create ./MyLibrary
Wrote 101032 entries to .mlqt/baseline.json, covering 104447 finding(s).
note: 3415 finding(s) share an entry with another. An entry is one rule + class + element + detail
      with no line number, so repeats of the same finding within a class collapse into one — a
      later check still reports and accepts every finding individually, so its accepted count is the
      larger number.

$ mlqt check ./MyLibrary --baseline .mlqt/baseline.json
note: baseline holds 101032 entries; one entry can cover several findings, so the accepted count
      below can be larger
No new findings (104447 finding(s) accepted as baseline debt) in 38112 model(s).
```

An entry is a **fingerprint**: rule id + class + element + detail, deliberately *without* a line
number, so it survives reformatting and edits elsewhere in the file. A rule that fires more than once
on the same element of the same class — two misplaced imports, the same misspelled word twice in one
description, the same broken `modelica://` link twice in a Documentation block — therefore produces
several findings but a single entry. Membership is by fingerprint, so all of them are accepted.

One consequence worth knowing: an entry only becomes prunable when the **last** finding sharing it is
fixed. Fix one of two identical-fingerprint findings and `prune` will correctly leave the entry alone.

**Severity plays no part in this.** Errors, warnings and infos are all recorded. The only findings a
baseline never holds are [parse diagnostics](#parse-diagnostics); `create` says so on stderr
when it skipped any.

### `prune` vs `update`

Both drop entries you have fixed. The difference is whether the command can **add**:

| | Drops fixed entries | Accepts new findings as debt |
|---|---|---|
| `prune` | yes | **no** — it can only ever shrink the baseline |
| `update` | yes | **yes**, and only with `--force` |

**`prune` is the safe maintenance command.** Run it whenever you like, or on a schedule: it banks the
debt you have paid off and cannot silently accept debt someone just added. When findings exist that it
left alone, it says so:

```
Pruned 1 entry now fixed from .mlqt/baseline.json; 1 entry remaining
1 entry is not in the baseline and will still fail the gate. Prune never accepts new debt;
`baseline update --force` would.
```

**`update` is a deliberate re-baseline** — for when you have enabled new rules and want their existing
findings accepted in one go. Because accepting a finding nobody reviewed is the one way to defeat
the ratchet by accident, it refuses unless you say so:

```
$ mlqt baseline update ./MyLibrary
error: this would absorb 1 entry not in the baseline, accepting it as debt.
       Re-run with --force if that is intended (e.g. you just enabled new rules).
       To only drop findings you have fixed, without accepting anything new, use `baseline prune`.

$ mlqt baseline update ./MyLibrary --force
Updated .mlqt/baseline.json: 2 entries covering 2 finding(s) — absorbed 1 entry as accepted debt,
dropped 1 entry now fixed
```

`--force` is only needed when there is something to absorb; an `update` that would merely drop fixed
entries behaves like `prune` and runs without it. **Never run `update --force` from CI** — that turns
the gate off one commit at a time.

`create`/`update`/`prune` accept `--baseline <path>` (default `<library-path>/.mlqt/baseline.json`)
and `--config <path>`; `create` refuses to overwrite an existing file unless `--force` is given.

The file records **when it was generated** and the **revision and branch** it describes, so a reviewer
can tell how old the accepted debt is and diff from there:

```json
{
  "version": 3,
  "createdUtc": "2026-08-21T12:33:47Z",
  "revision": "8481e74df0bce85be36974da7daaa57c8e44d90f",
  "branch": "main",
  "rules": { "MLQT.Doc.ClassDescription": "Warning" },
  "excludedLibraries": ["*_Tests"],
  "dependencies": ["Modelica", "ModelicaServices"],
  "findings": [ ... ]
}
```

### Rule drift

`rules` records which rules were in force. A later `check` compares them and warns when the
configuration has moved on, because both ways it can differ are otherwise silent:

- a rule **enabled since** the baseline reports its pre-existing findings as **new**, so a change
  looks like it caused a regression it had nothing to do with;
- a rule **disabled since** leaves entries that can never match again.

```
$ mlqt check ./MyLibrary --baseline .mlqt/baseline.json
warning: the baseline was generated with a different rule set
         enabled since: MLQT.Doc.ClassDescription
         severity changed: MLQT.Doc.ParameterDescription (Warning -> Error)
         Pre-existing findings of a newly enabled rule are reported as new.
         `mlqt baseline update --force` would accept them.
```

It is a warning, not a failure — the gate still means what it says. Changes to `ExcludedLibraries` and
to the loaded `dependencies` are reported too, since both make findings appear or disappear in exactly
the same way.
`prune` and `update` both refresh the record, so either resolves the warning.

A baseline written before version 3 has no `rules` to compare; the check says so once rather than
guessing.

`update` and `prune` refresh the stamp, because both rewrite the content. Outside a working copy the
revision fields are simply absent. A version-1 baseline (no metadata) still loads unchanged.
**Commit `.mlqt/baseline.json` to the repository** — it is a reviewable debt ledger, and its size
shrinking over time is your burndown.

Then gate on new findings only:

```bash
# Fail on NEW warnings; tolerate everything in the baseline
mlqt check <library-path> --baseline .mlqt/baseline.json --fail-on warning
```

Findings are classified as **new** (not in the baseline → gated), **accepted debt** (in the baseline,
unchanged model → never fails), or **touched debt** (in the baseline, but in a model this change
modified). The baseline uses a reformat-stable fingerprint, so reformatting a model does not turn its
accepted debt into new findings.

Parse diagnostics are never captured in a baseline and are always classified **new** — see
[Parse diagnostics](#parse-diagnostics).

### Changed-model escalation (the "boy-scout rule")

With `--changed-from <ref>`, existing debt in a model the change touched becomes **touched debt**.
Works with Git and SVN. The three policies are:

| Policy | Listed in the report? | Fails the gate? |
|--------|----------------------|-----------------|
| `warn` (default) | yes, tagged `[touched]` | no |
| `fail` | yes, tagged `[touched]` | yes |
| `ignore` | no — counted as accepted debt | no |

```bash
# Fail on new findings, and on pre-existing findings in models changed since main
mlqt check ./MyLibrary --baseline .mlqt/baseline.json --changed-from main \
      --touched-debt fail --fail-on warning
```

**Single-file libraries.** When a library is stored as one big `package.mo`, any edit marks *every*
model in it as changed, so all its baselined debt becomes touched debt and swamps the report. Use
`--touched-debt ignore` to silence it — new findings are still reported and still gate, and the
"Fixed in changed models" section still credits the debt you did clear:

```bash
mlqt check ./ExternData --baseline .mlqt/baseline.json --changed-from main \
      --touched-debt ignore --fail-on warning
```

## Recording the coverage trend

`--metrics` appends a point to `<library-path>/.mlqt/metrics-history.json` — the same file the desktop
app's **Coverage** dashboard plots. Running it per commit in CI builds the burndown automatically,
instead of it depending on someone remembering to press **Save snapshot**.

A point records the coverage percentages and their raw compliant/eligible counts, the class count, the
active style-finding count, and the **revision and branch** it was measured at. Recording happens
whatever the gate decides — a failing build is exactly the one whose numbers you want on the trend.

**Which dimensions appear** follows the rules the run has enabled. A rule set to `Off` is a decision
that its gap is not worth tracking, so it gets no dimension; enabling one later adds its dimension
from that point on, and earlier points simply have no value for it. The layout dimensions (imports
first, extends at top, sections, initial sections) are also left out when `applyFormattingRules` is on
together with `oneOfEachSection`, because the formatter rewrites all four on save — the number would
measure the moment before the save rather than the library. Coverage itself is measured from the
source, never from findings, so a waived or baselined finding still counts as a gap.

**Scopes.** One point is recorded for the whole checked set, plus one for each top-level library
package, each counting only its own classes:

```
note: recorded metrics for all libraries, Modelica, ModelicaServices, ModelicaTest in …
```

This is what the dashboard's scope filter reads — it matches a point's scope against the selected
package id exactly, so without the per-library points a library shows its current coverage but an
empty trend. Only packages get their own scope: a flat folder of loose `.mo` files records the
whole-set point alone, since the dashboard's picker only offers packages anyway. Each scope skips
independently when its numbers have not moved.

```bash
mlqt check ./MyLibrary --baseline .mlqt/baseline.json --metrics
```

### It will not loop

The obvious worry: CI writes the history file, commits it to share it, that commit triggers CI, which
writes the file again — forever.

`--metrics` **skips a point that says nothing new**, which breaks the cycle without depending on your
CI system's path filters or `[skip ci]` conventions being right:

- if the history already has a point for this **revision**, nothing is written (a rebuild or retry of
  the same commit does not stack duplicate points);
- if the numbers are **identical to the previous point**, nothing is written.

So the CI-commit build measures the same library, finds the same numbers, writes nothing, and commits
nothing. The cycle ends after one extra run. `--metrics-force` overrides this; do not use it in a job
that commits the file.

### Choosing how to share the history

| Approach | How | Trade-off |
|---|---|---|
| **Commit it** (default) | `--metrics`, then commit `.mlqt/metrics-history.json` if it changed | Everyone sees the trend in the desktop app. Needs the commit step; relies on the skip rule above (plus, ideally, a path filter) to stay quiet. |
| **Keep it as an artifact** | `--metrics-out $CI_ARTIFACTS/metrics-history.json` | No commits at all, so no loop is even possible. The trend lives in CI, not in the desktop app, and you must carry the previous file into the next run for it to accumulate. |
| **Commit from one branch only** | `--metrics` in the default-branch job only | Trend follows the mainline rather than every PR. Simplest thing that stays useful. |

If you commit from CI, belt and braces is worth it: exclude `.mlqt/metrics-history.json` from your
build trigger's path filter, and/or put `[skip ci]` in the commit message. The skip rule means you are
safe without them, but with them the extra build never happens at all.

## Output formats

- **console** — human-readable, grouped by model (each model header shows its file), with a
  per-severity summary.
- **json** — an object with `tool`, `library`, `modelsChecked`, `findingCount`, a `summary` (new /
  accepted / touched counts), and a `findings` array. Each finding includes its `Fingerprint`, `Status`
  (`New`/`AcceptedDebt`/`TouchedDebt`), `File`, `Line` (the line in that file) and `ModelLine` (the
  same finding's line within the class's own source, for a tool that navigates by class).
- **junit** — JUnit XML where each actionable finding is a failing test case. Renders in the native
  test-report UI of most CI systems (TeamCity, Jenkins, GitLab, Azure DevOps) with no extra integration.
- **sarif** — SARIF 2.1.0 for GitHub code scanning / Azure DevOps. `level` reflects the rule's
  configured severity, `baselineState` marks new vs unchanged, and `partialFingerprints` lets viewers
  match results across runs. File paths are relative to the library; when the library is a
  subdirectory of the repository, pass `--sarif-base <repo-root>` so they are relative to the root the
  reader resolves against — otherwise GitHub attaches the annotations to nothing. A relative
  `--sarif-base` is resolved against the library (`--sarif-base ..` for a library one level down), and
  a base that does not contain the library is refused rather than written as `../…`, which GitHub
  rejects.
  The document is validated against the SARIF 2.1.0 schema on every push — see
  `build/validate-sarif.ps1`, which also runs locally.
- **teamcity** — TeamCity service messages: `buildStatisticValue` lines (so TeamCity graphs the
  baseline-debt trend over builds), a message per actionable finding, and a `buildProblem` when the
  gate fails.
- **markdown** — a PR-comment-ready summary table (counts, gate result, actionable findings).

### SARIF and GitHub

GitHub code scanning reads a documented subset of SARIF, and two things about that subset change what
MLQT writes.

**Accepted debt is left out by default.** GitHub supports neither `baselineState` nor `suppressions`,
so a finding written to SARIF becomes an open alert whatever it is tagged with. Uploading accepted
debt therefore fills the Security tab with alerts for debt the team has already agreed to, and buries
the findings the run is about. When a baseline is in use, SARIF carries the new and touched-debt
findings only, and the run says how many it left out:

```
note: 5 accepted-debt finding(s) left out of the SARIF - GitHub has no way to show them as
      accepted, so they would arrive as open alerts. --sarif-include-accepted keeps them
```

Pass `--sarif-include-accepted` for a consumer that does honour `baselineState` — the findings are
still tagged with it either way. Every other format (console, JSON, JUnit, markdown, TeamCity)
reports accepted debt as it always did; this is a display decision about one consumer, not about the
findings.

**Result limits.** GitHub rejects an upload of more than 25,000 results and displays only the first
5,000 of a run. MLQT says so when a report crosses either threshold, because the symptom otherwise is
an empty Security tab with nothing to explain it. A baseline is usually the answer: it is the
difference between uploading a library's whole history and uploading what a change did.

**Rule metadata.** Each rule carries `shortDescription`, `fullDescription` and `help.text` /
`help.markdown` — the last is what renders in the alert body, so an alert says what the rule wants
and where to configure it rather than only naming an id. The `helpUri` points at the settings
reference, and the rule's category is emitted as a tag so alerts can be filtered by it.

### Gating on coverage

`--fail-on` answers "did this change introduce findings". The coverage numbers answer a different
question — "is this library documented well enough" — and `--min-coverage` gates on it:

```bash
mlqt check ./MyLibrary --min-coverage 80                        # every tracked dimension
mlqt check ./MyLibrary --min-coverage 80 --min-coverage class-description=95
```

A named dimension overrides the blanket figure, which is the shape a real policy takes ("80%
everywhere, 95% on descriptions"). Dimensions are named as the dashboard shows them, however you
prefer to spell it: `class-description`, `ClassDescription` and `"Class description"` all resolve to
the same one. The names are `class-description`, `documentation-info`, `documentation-revisions`,
`icon`, `parameter-description`, `constant-description`, `unit`, and the layout dimensions
(`imports-first`, `extends-at-top`, `one-of-each-section`, `initial-sections-first`,
`initial-sections-last`, `equation-algorithm-not-mixed`, `connections-not-mixed`).

**The ratchet, applied to coverage.** For a legacy library, a threshold it does not meet yet is not
much use. `--coverage-ratchet` requires only that nothing goes backwards:

```bash
mlqt check ./MyLibrary --coverage-ratchet --metrics
```

It compares each dimension against the last snapshot recorded for the whole checked set in
`.mlqt/metrics-history.json` (or `--metrics-out`), so pair it with `--metrics` — each run records
the point the next one is measured against. With no history yet it says so and passes: the first run
has nothing to go backwards from.

Notes:

- A coverage gate is **independent of `--fail-on`**. Switching findings off with `--fail-on off`
  says findings do not fail this build; it does not withdraw a coverage requirement you also asked
  for. Either gate failing exits `1`.
- A dimension a repository does not track — its rule is switched off — is **warned about**, not
  silently skipped: a requirement that checks nothing is the failure a quality gate can least
  afford. An unknown dimension name is a usage error (exit `2`).
- The verdicts appear in the JSON report as a `coverageGate` array (dimension, percent, required,
  `threshold`/`previous`, passed), and on stderr as one line per failure.

### Several reports from one run

A pipeline usually wants two reports: one a person reads in the build log, one a machine reads.
`--report <format>:<path>` writes an extra one to a file alongside the primary output, and can be
repeated:

```bash
# A readable log on stdout, a JUnit file for the test UI, and SARIF for code scanning — one check
mlqt check ./MyLibrary --fail-on warning \
      --report junit:mlqt-results.xml --report "sarif:mlqt.sarif"
```

Running the check twice to get two formats costs the load and the check twice over — minutes on a
large library — and the two runs can disagree if anything on disk changed between them, which is
precisely when the reports are being trusted. Both files come from the same findings here.

`--format`/`--out` still control the primary output, so existing invocations are unchanged. Paths are
taken as given (relative to the working directory, like `--out`); two reports may not name the same
file. A `console` report written to a file carries no colour codes. The exit code is the gate's, no
matter how many reports were written.

### CI examples

```bash
# Fail the build on warnings, and publish findings as a JUnit report
mlqt check ./MyLibrary --fail-on warning --format junit --out mlqt-results.xml
```

```bash
# Machine-readable output for custom processing
mlqt check ./MyLibrary --format json --out findings.json
```

## Running the check before each commit

The gate in CI is the one that counts, but it is the slowest place to find out. `mlqt hook` installs
the same check as a git `pre-commit` hook, so a finding is caught while the fix is still a keystroke
away rather than a second commit after a build has failed.

```bash
mlqt hook install ./MyLibrary            # from anywhere; the repository is found from the library
mlqt hook status ./MyLibrary
mlqt hook uninstall ./MyLibrary
```

The library path defaults to the current directory, so standing in your repository `mlqt hook install`
is usually the whole command. The repository is located by walking up from the library, so a library
in a subdirectory needs nothing extra, and a worktree or submodule (whose `.git` is a file) is
followed to the directory git actually reads hooks from.

| Option | Description | Default |
|--------|-------------|---------|
| `--fail-on off\|warning\|error` | What blocks the commit | `error` |
| `--baseline <path>` | Classify against a baseline, so accepted debt does not block every commit | none |
| `--changed-from <ref>` | Escalate debt in models changed since this ref | none |
| `--dependency <path>` | Load another library so references resolve. Repeatable | none |
| `--force` | Replace (or delete) a `pre-commit` hook mlqt did not write | off |

The options are baked into the generated script; re-run `mlqt hook install` to change them.

**What the hook does.** It exits immediately unless the staged change touches a `.mo` file, so
commits it has nothing to say about cost nothing. Otherwise it runs `mlqt check` over the library
with the options above and blocks the commit on a non-zero exit — including exit `2`, because a check
that could not run has not approved anything.

**Getting past it.** `git commit --no-verify` skips every hook, and that is the deliberate escape
hatch: a hook that cannot be bypassed gets deleted instead. For a finding that is correct behaviour
rather than a mistake, waive it in the source with `__MLQT(suppress="<rule>")` — see
[Suppressing intentional findings](ci-quality-gate.md#suppressing-intentional-findings) — so the
waiver is reviewed with the code and holds in CI too.

**Hooks are local.** `.git/hooks` is not committed, so each person installs their own; the hook is a
convenience for the author, never the enforcement. Keep the CI gate.

**A hook someone else wrote is left alone.** Install and uninstall both refuse when the existing
`pre-commit` does not carry mlqt's marker line, rather than overwriting a colleague's or a
framework's. Add the check to that script yourself, or pass `--force`. If your repository uses
`core.hooksPath` (husky, pre-commit, lefthook), call `mlqt check` from your existing configuration
instead — git reads hooks only from the configured directory, and the one installed here would sit
unused.

**Git only.** SVN has no client-side hooks: a pre-commit hook there runs on the server and would need
MLQT installed on it. Outside a git working copy the command says so rather than writing a file
nothing will read. For SVN, the desktop app's commit dialog is where the check runs.

## Comparing two copies of a library

```
mlqt compare <library-a> <library-b> [--format console|json] [--out <file>] [--no-added]
```

Lists the classes `<library-a>` has that `<library-b>` does not. It is for the question that follows a
bulk edit — a reformat, a restructure, a big merge — when the class count has dropped and you need to
know *which* classes went, out of several thousand.

Classes are matched on their **full Modelica name** (`Modelica.Blocks.Continuous.PID`) and on nothing
else. How they are laid out on disk is free to have changed completely: splitting a package file into
a directory with one file per class, or collapsing one back, produces no differences at all.

No settings are read and no style rules are run. Both libraries are simply loaded and their class
inventories compared, so the command is much faster than a check.

| Option | Description | Default |
|--------|-------------|---------|
| `--format console\|json` | Output format | `console` |
| `--out <file>` | Write the report to a file instead of stdout | stdout |
| `--no-added` | List only what is missing, not what B gained | both are listed |

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | Every class in A is present in B |
| `1` | Classes are missing from B |
| `2` | Usage or load error (bad path, no library found there) |

Classes that only B has never fail the command — gaining a class is not a loss.

### Reading the report

```
Comparing class inventories
  A  C:/Libraries/MyLibrary-before
     8534 classes in MyLibrary
  B  C:/Libraries/MyLibrary-after
     8501 classes in MyLibrary

warning: 1 file(s) in B could not be parsed, so every class they hold is counted as absent:
           MyLibrary/Blocks/Continuous.mo

33 classes are missing from B:

  MyLibrary.Blocks.Continuous.PID    model     MyLibrary/Blocks/Continuous.mo:412
  MyLibrary.Blocks.LimPID            model     MyLibrary/Blocks.mo:88
      -> B has a new class of this name: LimPID

1 class is only in B:

  LimPID                             model     MyLibrary/Blocks/LimPID.mo:1
      -> A has a class of this name that B is missing: MyLibrary.Blocks.LimPID

8534 classes in A, 8501 in B - 33 missing, 1 added
```

Three things in that report are worth knowing about:

- **The file and line are A's**, not B's — they say where the class was, which is where you go to get
  it back.
- **`->` lines are leads, not conclusions.** A class listed as missing whose simple name turns up as a
  *new* class in B is usually the same class re-rooted: most often its `within` clause was lost, so
  `MyLibrary.Blocks.LimPID` came back as plain `LimPID`. That is one class showing up twice — once as
  missing, once as added — and it is why the added list is on by default.
- **Unparseable files are called out first.** A file the parser cannot get a class out of looks exactly
  like a file whose classes were all deleted, and a bulk edit is the most likely thing to have left
  one. Fix those before reading anything else in the list.

```bash
# Did the reformat lose anything?
mlqt compare ./MyLibrary-before ./MyLibrary-after --out missing.txt
```

```bash
# Machine-readable, for a script that has to act on the list
mlqt compare ./MyLibrary-before ./MyLibrary-after --format json --out missing.json
```
