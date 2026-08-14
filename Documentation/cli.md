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
| `--touched-debt warn\|fail\|ignore` | Existing debt in a model the change touched | `warn` |
| `--format console\|json\|junit` | Output format | `console` |
| `--out <file>` | Write output to a file instead of stdout | stdout |
| `--fail-on off\|warning\|error` | Exit non-zero when findings reach this level | `error` |
| `--no-color` | Disable coloured console output (also honours `NO_COLOR`) | colour on a TTY |
| `-h`, `--help` | Show help | |

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | No findings at or above `--fail-on` |
| `1` | Findings at or above `--fail-on` |
| `2` | Usage or load error (bad path, unreadable/invalid config) |

Because the built-in rules currently report at **warning** severity, the default `--fail-on error`
is effectively report-only (it surfaces findings but exits `0`). Use `--fail-on warning` for a
strict gate, or `--fail-on off` to never fail.

## Settings

The rules that run are controlled by a `StyleCheckingSettings` JSON file — the same format the
desktop app writes to `<repo>/.mlqt/settings.json`. If no config is found, no rules are enabled and
no findings are produced. See [settings-reference.md](settings-reference.md).

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
mlqt baseline update <library-path>     # regenerate the baseline
mlqt baseline prune  <library-path>     # drop entries whose findings are now fixed
```

`create`/`update`/`prune` accept `--baseline <path>` (default `<library-path>/.mlqt/baseline.json`)
and `--config <path>`; `create` refuses to overwrite an existing file unless `--force` is given.
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

### Changed-model escalation (the "boy-scout rule")

With `--changed-from <ref>`, existing debt in a model the change touched becomes **touched debt**.
By default it is reported but does not fail (`--touched-debt warn`); use `--touched-debt fail` to
require cleanup of pre-existing issues in files you edit. Works with Git and SVN.

```bash
# Fail on new issues, and on pre-existing issues in models changed since main
mlqt check ./MyLibrary --baseline .mlqt/baseline.json --changed-from main \
      --touched-debt fail --fail-on warning
```

## Output formats

- **console** — human-readable, grouped by file, with a per-severity summary.
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
