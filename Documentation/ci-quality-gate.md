# Setting up the MLQT CI quality gate

A hands-on guide to running MLQT's style/analysis checks in CI and gating on **new** issues, so you
can trial it on a real Modelica library. For the full option reference see [cli.md](cli.md).

## What it does — the ratchet

`mlqt check` runs MLQT's static-analysis rules over a library and reports findings. A large legacy
library will have many findings; nobody fixes them all at once. So you:

1. **Baseline** the current findings — they become *accepted debt* and never fail the build.
2. **Gate** on findings that are *not* in the baseline — only genuinely new issues fail CI.
3. Optionally **escalate** pre-existing issues in models a change touched (the "boy-scout rule").

Findings are classified as **new** (fail), **accepted debt** (tolerated), or **touched debt**
(in a model the change modified). Identity is a reformat-stable fingerprint, so reformatting a model
never turns its accepted debt into new findings.

---

## 1. Get the `mlqt` command

The CLI is a .NET tool (`MLQT.Cli`, command `mlqt`); it is not yet published to a public feed, so
build it from this repository.

**Quick local testing** (no install):

```bash
dotnet run --project MLQT.Cli -- check /path/to/MyLibrary
```

**Install as a tool** (gives you a real `mlqt` command):

```bash
dotnet pack MLQT.Cli/MLQT.Cli.csproj -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg MLQT.Cli    # then: mlqt ...
# update later:  dotnet tool update --global --add-source ./nupkg MLQT.Cli
```

**For a CI agent** (isolated, no global state):

```bash
dotnet tool install --tool-path ./tools --add-source ./nupkg MLQT.Cli
./tools/mlqt check /path/to/MyLibrary
```

### Which one should I use?

All three run **identical code** — the difference is only distribution and ergonomics:

- `dotnet run` builds and runs from the source tree in place. It needs the repository present and
  rebuilds each time. Best for a **quick local trial while you're in the repo**.
- `dotnet pack` wraps the build output into a single versioned NuGet package (`.nupkg`);
  `dotnet tool install` extracts it into a per-user tool store and puts an **`mlqt` command on your
  PATH**, decoupled from the repo. Best when you want to **run against many libraries** or on a
  **machine/CI agent that doesn't have the source**. The `--tool-path ./tools` variant keeps the
  install in a throwaway folder with no global state — the CI sweet spot.

Note that `dotnet tool` is **framework-dependent** either way: it is not a self-contained or
single-file binary, so the .NET 10 runtime must be installed. (Standalone per-OS binaries are a
separate, later packaging concern.) For local testing on your own machine you can skip packing
entirely and just use `dotnet run`.

### Point it at your repository root

`mlqt check <path>` discovers and checks **every** Modelica library under the path, the same way the
MLQT desktop app treats a repository:

- if the path has a `package.mo`, it is a single library (its sub-packages are included);
- otherwise, each immediate sub-directory with a `package.mo` is a library, plus any loose top-level
  `.mo` files.

So you normally point it at the **repository root** and check the whole thing in one run — whether the
repo is a single library or several side by side. A single `<root>/.mlqt/settings.json` and
`<root>/.mlqt/baseline.json` apply to the whole repository (again, matching the desktop app).

---

## 2. First run

```bash
mlqt check /path/to/MyLibrary
```

Out of the box **no rules are enabled**, so you'll see `note: no style rules are enabled`. Enable
rules next.

---

## 3. Choose your rules — `.mlqt/settings.json`

Create `<library-root>/.mlqt/settings.json` and turn on the rules you want. **Commit this file** —
it is the shared configuration for everyone and for CI.

```jsonc
{
  "ClassHasDescription": true,
  "ParameterHasDescription": true,
  "ConstantHasDescription": true,
  "ImportStatementsFirst": true,
  "OneOfEachSection": true,

  // Optional: make specific rules hard errors (default is Warning).
  "RuleSeverities": {
    "MLQT.Doc.ClassDescription": "Error"
  }
}
```

Each rule is enabled by a boolean and identified by a rule id (used in `RuleSeverities`, and shown in
every report):

| Setting (`.mlqt/settings.json`) | Rule id | Checks |
|---|---|---|
| `ClassHasDescription` | `MLQT.Doc.ClassDescription` | Class has a description string |
| `ClassHasDocumentationInfo` | `MLQT.Doc.ClassDocumentationInfo` | Class has `Documentation(info=…)` |
| `ClassHasDocumentationRevisions` | `MLQT.Doc.ClassDocumentationRevisions` | Class has `Documentation(revisions=…)` |
| `ClassHasIcon` | `MLQT.Doc.ClassIcon` | Class has an `Icon` (own or inherited) |
| `ParameterHasDescription` | `MLQT.Doc.ParameterDescription` | Public parameters have a description |
| `ConstantHasDescription` | `MLQT.Doc.ConstantDescription` | Public constants have a description |
| `ImportStatementsFirst` | `MLQT.Style.ImportStatementsFirst` (+ `MLQT.Style.ExtendsAtTop`) | Imports before the rest; extends at top |
| `OneOfEachSection` | `MLQT.Style.OneOfEachSection` | No more than one of each section |
| `DontMixEquationAndAlgorithm` | `MLQT.Style.DontMixEquationAndAlgorithm` | Don't mix equation and algorithm sections |
| `DontMixConnections` | `MLQT.Style.DontMixConnections` | Don't mix `connect` and equations |
| `InitialEQAlgoFirst` | `MLQT.Style.InitialEqAlgoFirst` | Initial sections before regular ones |
| `InitialEQAlgoLast` | `MLQT.Style.InitialEqAlgoLast` | Initial sections after regular ones |
| `FollowNamingConvention` | `MLQT.Naming.Convention` | Names follow the configured convention |
| `SpellCheckDescription` | `MLQT.Spelling.Description` | Descriptions are spelled correctly |
| `SpellCheckDocumentation` | `MLQT.Spelling.Documentation` | Documentation is spelled correctly |
| `ValidateModelReferences` | `MLQT.Reference.ModelReferences` | `modelica://` model references resolve |

Severities in `RuleSeverities` are `Off`, `Info`, `Warning`, or `Error`; the map wins over the
booleans. Enabled rules default to `Warning`.

**Recommended starting set for a first trial:** the self-contained documentation/ordering rules above.
Add these with care:

- **`ValidateModelReferences`** — the CLI loads only the library you point it at, **not its
  dependencies** (MSL, commercial libraries). A `modelica://` reference into a library that isn't
  loaded will currently be reported as broken. Leave it off until you load dependencies alongside your
  library, or expect false positives.
- **`SpellCheckDescription` / `SpellCheckDocumentation`** — will flag domain terms until you build up a
  custom dictionary. See [spell-checking.md](spell-checking.md).
- **`FollowNamingConvention`** — needs a naming convention configured. See
  [naming-conventions.md](naming-conventions.md) and [settings-reference.md](settings-reference.md).

Run again and review what it finds:

```bash
mlqt check /path/to/MyLibrary
```

---

## 4. Baseline your existing debt

Snapshot everything currently found as *accepted debt*, then **commit the baseline**:

```bash
mlqt baseline create /path/to/MyLibrary          # writes <root>/.mlqt/baseline.json
git add .mlqt/baseline.json && git commit -m "Add MLQT baseline"
```

`.mlqt/baseline.json` is a reviewable ledger — one entry per finding with its rule id, model, and
message. Its size shrinking over time is your debt burndown. (`baseline create` refuses to overwrite
an existing file; use `baseline update` to regenerate or `--force`.)

---

## 5. Gate on new issues

This is the command CI runs:

```bash
mlqt check /path/to/MyLibrary --baseline .mlqt/baseline.json --fail-on warning
```

Right after baselining, everything is accepted debt, so this **passes (exit 0)**. Introduce a new
violation (e.g. add an undescribed public parameter) and it **fails (exit 1)** — only the new finding
is reported as `new`.

Exit codes: **0** = passed, **1** = findings at/above `--fail-on`, **2** = usage/load error.

`--fail-on` controls the threshold: `warning` (fail on any new finding), `error` (fail only on new
findings from rules you set to `Error`), or `off` (never fail — report only). Start with `warning` for
the ratchet.

---

## 6. (Optional) Escalate debt in changed models

To also push cleanup of pre-existing issues in models a change touched, diff against a ref:

```bash
mlqt check /path/to/MyLibrary --baseline .mlqt/baseline.json \
      --changed-from main --touched-debt fail --fail-on warning
```

`--changed-from <ref>` marks findings in models changed since `<ref>` as **touched debt**; with
`--touched-debt fail` those also fail the gate (default `warn` reports them without failing). Works
with Git and SVN — run it from inside the working copy.

**What `<ref>` should be** differs by VCS:

- **Git** — a branch, tag, or commit: `--changed-from main`, `--changed-from origin/main`,
  `--changed-from HEAD~1`.
- **SVN** — a **revision, not a branch name** (SVN branches are directories, not revisions, so a
  branch name is rejected with `could not resolve revision`). Pass either:
  - a **revision number** — `--changed-from 4567` (changes since that revision), or
  - a **keyword** — `BASE` (only your *uncommitted* local changes), or `HEAD` / `PREV` / `COMMITTED`.

  For a pre-commit gate use `--changed-from BASE`; to compare against a baseline revision (e.g. where
  a release was branched), pass that revision number. (Comparing to another SVN *branch* is not
  supported yet.)

MLQT prints a diagnostic note so you can see what the diff detected:

```
note: 3 changed .mo file(s), 5 model(s) changed since main
```

If `<ref>` can't be resolved (e.g. your checkout has `master`, or only `origin/main`, not a local
`main`), the command **errors** rather than silently treating nothing as changed. If the note shows
`0 model(s) changed`, the ref probably already contains your change — diff against a ref that is
*behind* it (`--changed-from HEAD~1`, `origin/main`, …).

---

## 7. Wire it into CI

Pick the integration that matches your system. All of them rely on the **exit code** to pass/fail the
build; the format just controls how findings are surfaced.

### Any CI (exit code + JUnit report)

Emit a JUnit report and point your CI's test-report step at it — findings then show up in the native
test UI (Jenkins, GitLab, Azure DevOps, TeamCity, …):

```bash
mlqt check ./MyLibrary --baseline .mlqt/baseline.json --fail-on warning \
      --format junit --out mlqt-results.xml
```

A non-zero exit fails the build step.

### TeamCity (native)

Use the TeamCity format — it prints service messages that TeamCity reads straight from the build log:

```bash
mlqt check ./MyLibrary --baseline .mlqt/baseline.json --fail-on warning --format teamcity
```

You get:
- `buildStatisticValue` lines for new / accepted / touched counts — add them as **custom charts** and
  TeamCity graphs your debt trend across builds automatically;
- a message per actionable finding;
- a `buildProblem` (and non-zero exit) that fails the build when the gate fails.

### GitHub

- **With code scanning / Actions:** emit SARIF and upload it, so findings appear as PR annotations:
  ```bash
  mlqt check ./MyLibrary --baseline .mlqt/baseline.json --fail-on warning \
        --format sarif --out mlqt.sarif
  ```
- **Without Actions:** emit a markdown summary and post it as a PR comment from your runner/script:
  ```bash
  mlqt check ./MyLibrary --baseline .mlqt/baseline.json --fail-on warning \
        --format markdown --out mlqt.md
  ```
  The exit code still gates; the markdown gives reviewers a readable summary.

---

## 8. Maintaining the baseline

- **When you fix issues:** `mlqt baseline prune <root>` drops entries whose findings are now fixed,
  shrinking the ledger. Commit the result.
- **After deliberately accepting more debt** (e.g. a big import): `mlqt baseline update <root>` and
  review the diff — the baseline growing is visible in code review.
- **Never** let the baseline grow silently: keep it under review, and let `prune` pull the numbers down
  as the library improves.

---

## Command & flag reference

```
mlqt check <library-path> [--baseline <path>] [--changed-from <ref>]
                          [--touched-debt warn|fail|ignore]
                          [--config <path>] [--format console|json|junit|sarif|teamcity|markdown]
                          [--out <file>] [--fail-on off|warning|error] [--no-color]

mlqt baseline create|update|prune <library-path> [--baseline <path>] [--config <path>] [--force]
```

`--config` defaults to `<library-path>/.mlqt/settings.json`; `--baseline` for the `baseline` commands
defaults to `<library-path>/.mlqt/baseline.json`. A **relative** `--config`/`--baseline` value is
resolved against the library/repository path (not the current directory), so
`--baseline .mlqt/baseline.json` finds `<repo>/.mlqt/baseline.json` from any working directory;
absolute paths are used as-is.

---

## Known limitations

- **Line numbers are relative to each model's definition, not the file.** For a model nested in a
  multi-model file (e.g. a `package.mo`), a finding's line number counts from the start of that
  model's own definition, not from the top of the file. This is fine when you fix issues in a
  Modelica tool (which navigates by model/class, so a line *within the model* is what you want), and
  reports now show the model each finding belongs to. It does mean the line won't line up with the
  raw file — most relevant for SARIF/GitHub code-scanning annotations, which are file-line based. A
  future change may map finding lines to file-absolute positions; revisit if SARIF annotations need
  to point at exact file lines.
- **Dependencies aren't loaded** — see `ValidateModelReferences` above.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `note: no style rules are enabled` | No rules on in `.mlqt/settings.json` (or no settings file). Enable rules (§3). |
| Lots of `MLQT.Reference.ModelReferences` findings | Dependencies (MSL, etc.) aren't loaded, so external `modelica://` refs look broken. Turn `ValidateModelReferences` off for now. |
| Spelling flags valid domain terms | Build a custom dictionary — see [spell-checking.md](spell-checking.md). |
| `error: '<path>' is not inside a Git or SVN working copy` | `--changed-from` needs to run inside the VCS working copy. |
| `error: could not resolve revision '<ref>'` | Git: the ref doesn't exist locally (wrong branch name, or only `origin/<ref>`) — try `origin/main`, `master`, `HEAD~1`. SVN: `--changed-from` must be a revision number or keyword (`BASE`/`HEAD`/`PREV`), **not a branch name**. |
| Only new findings show, no touched debt | The note says `0 model(s) changed` — the ref found no changes (it may already contain your change). Diff against a ref *behind* it. |
| `error: baseline not found` | The `--baseline` path is wrong, or you haven't run `baseline create` yet. |
| Gate passes but you expected a failure | Findings default to `Warning`; use `--fail-on warning`, or set the rule to `Error` in `RuleSeverities` and use `--fail-on error`. |
