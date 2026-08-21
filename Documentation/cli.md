# MLQT CLI (`mlqt`)

A headless, cross-platform command-line tool that style-checks a Modelica library and reports
findings. It runs the same checks as the MLQT desktop app, with no UI and no MAUI dependency, so it
works on Windows, Linux, and macOS and is suited to CI pipelines.

> For a step-by-step guide to setting up the CI quality gate on a real library (enable rules,
> baseline existing debt, gate on new issues, wire into TeamCity/GitHub), see
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

| Option | Description | Default |
|--------|-------------|---------|
| `--config <path>` | Settings file to use | `<library-path>/.mlqt/settings.json`, else built-in defaults |
| `--baseline <path>` | Classify findings against a baseline (new vs accepted debt) | none |
| `--changed-from <ref>` | VCS ref to diff against, to escalate debt in changed models | none |
| `--touched-debt warn\|fail\|ignore` | Existing debt in a model the change touched: report it, gate on it, or leave it out of the report entirely | `warn` |
| `--format console\|json\|junit` | Output format | `console` |
| `--out <file>` | Write output to a file instead of stdout | stdout |
| `--fail-on off\|warning\|error` | Exit non-zero when findings reach this level | `error` |
| `--no-color` | Disable coloured console output (also honours `NO_COLOR`) | colour on a TTY |
| `--metrics` | Record a coverage snapshot in `<library-path>/.mlqt/metrics-history.json` | off |
| `--metrics-out <path>` | Record it somewhere else instead (implies `--metrics`) | — |
| `--metrics-force` | Record even when the numbers are unchanged (implies `--metrics`) | off |
| `-h`, `--help` | Show help | |

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | No findings at or above `--fail-on` |
| `1` | Findings at or above `--fail-on` |
| `2` | Usage or load error (bad path, unreadable/invalid config) |

Because the built-in **style** rules report at **warning** severity, the default `--fail-on error`
is effectively report-only for them (it surfaces findings but exits `0`). Use `--fail-on warning` for
a strict gate, or `--fail-on off` to never fail. **Parse diagnostics are the exception** — they are
errors and fail even the default gate; see below.

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

The same diagnostics appear in the desktop app's Issues panel and from the MCP server's `check_class`
/ `check_library`, with identical wording and line numbers.

## Settings

The rules that run are controlled by a `StyleCheckingSettings` JSON file — the same format the
desktop app writes to `<repo>/.mlqt/settings.json`. If no config is found, no style rules are enabled
and only parse diagnostics are produced. See [settings-reference.md](settings-reference.md).

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

## Baseline / ratchet

A large existing library usually has many findings that no one will fix all at once. A **baseline**
records the current findings as *accepted debt* so CI fails only on **new** issues.

```bash
mlqt baseline create <library-path>     # snapshot current findings -> <library-path>/.mlqt/baseline.json
mlqt baseline prune  <library-path>     # drop entries whose findings are now fixed
mlqt baseline update <library-path>     # regenerate: drop fixed entries AND accept new ones as debt
```

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
Pruned 1 fixed entry from .mlqt/baseline.json; 1 remain
1 finding(s) are not in the baseline and will still fail the gate. Prune never accepts new debt;
`baseline update --force` would.
```

**`update` is a deliberate re-baseline** — for when you have enabled new rules and want their existing
violations accepted in one go. Because accepting a violation nobody reviewed is the one way to defeat
the ratchet by accident, it refuses unless you say so:

```
$ mlqt baseline update ./MyLibrary
error: this would absorb 1 finding(s) that are not in the baseline, accepting them as debt.
       Re-run with --force if that is intended (e.g. you just enabled new rules).
       To only drop findings you have fixed, without accepting anything new, use `baseline prune`.

$ mlqt baseline update ./MyLibrary --force
Updated .mlqt/baseline.json: 2 finding(s) — absorbed 1 new as accepted debt, dropped 1 fixed
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
  "findings": [ ... ]
}
```

### Rule drift

`rules` records which rules were in force. A later `check` compares them and warns when the
configuration has moved on, because both ways it can differ are otherwise silent:

- a rule **enabled since** the baseline reports its pre-existing violations as **new**, so a change
  looks like it caused a regression it had nothing to do with;
- a rule **disabled since** leaves entries that can never match again.

```
$ mlqt check ./MyLibrary --baseline .mlqt/baseline.json
warning: the baseline was generated with a different rule set
         enabled since: MLQT.Doc.ClassDescription
         severity changed: MLQT.Doc.ParameterDescription (Warning -> Error)
         Pre-existing violations of a newly enabled rule are reported as new.
         `mlqt baseline update --force` would accept them.
```

It is a warning, not a failure — the gate still means what it says. Changes to `ExcludedLibraries` are
reported too, since un-excluding a library makes its findings appear as new in exactly the same way.
`prune` and `update` both refresh the record, so either resolves the warning.

A baseline written before version 3 has no `rules` to compare; the check says so once rather than
guessing.

`update` and `prune` refresh the stamp, because both rewrite the content. Outside a working copy the
revision fields are simply absent. A version-1 baseline (no metadata) still loads unchanged.
**Commit `.mlqt/baseline.json` to the repository** — it is a reviewable debt ledger, and its size
shrinking over time is your burndown.

Then gate on new issues only:

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
# Fail on new issues, and on pre-existing issues in models changed since main
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
  (`New`/`AcceptedDebt`/`TouchedDebt`), and `File`.
- **junit** — JUnit XML where each actionable finding is a failing test case. Renders in the native
  test-report UI of most CI systems (TeamCity, Jenkins, GitLab, Azure DevOps) with no extra integration.
- **sarif** — SARIF 2.1.0 for GitHub code scanning / Azure DevOps. `level` reflects the rule's
  configured severity, `baselineState` marks new vs unchanged, and `partialFingerprints` lets viewers
  match results across runs.
- **teamcity** — TeamCity service messages: `buildStatisticValue` lines (so TeamCity graphs the
  baseline-debt trend over builds), a message per actionable finding, and a `buildProblem` when the
  gate fails.
- **markdown** — a PR-comment-ready summary table (counts, gate result, actionable findings).

### CI examples

```bash
# Fail the build on warnings, and publish findings as a JUnit report
mlqt check ./MyLibrary --fail-on warning --format junit --out mlqt-results.xml
```

```bash
# Machine-readable output for custom processing
mlqt check ./MyLibrary --format json --out findings.json
```
