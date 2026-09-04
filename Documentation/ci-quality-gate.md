# Setting up the MLQT CI quality gate

A hands-on guide to running MLQT's style/analysis checks in CI and gating on **new** findings, so you
can trial it on a real Modelica library. For the full option reference see [cli.md](cli.md).

## What it does — the ratchet

`mlqt check` runs MLQT's static-analysis rules over a library and reports findings. A large legacy
library will have many findings; nobody fixes them all at once. So you:

1. **Baseline** the current findings — they become *accepted debt* and never fail the build.
2. **Gate** on findings that are *not* in the baseline — only genuinely new findings fail CI.
3. Optionally **escalate** pre-existing findings in models a change touched (the "boy-scout rule").

Parse errors are reported separately from all of this and always fail — see
[Parse errors always fail](#parse-errors-always-fail).

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

Out of the box **no style rules are enabled**, so you'll see `note: no style rules are enabled`.
Enable rules next.

One thing is reported even with no rules configured: a **parse error**. If a file has a syntax error
you will see it on this very first run, and the command will exit `1`. That is deliberate — see
[Parse errors always fail](#parse-errors-always-fail).

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

**Leave out the libraries you don't want judged.** A repository usually holds test-case and example
libraries next to the ones under development. List them and they are loaded but never reported on —
while still counting as users of the code they exercise, so excluding them cannot make that code look
unused:

```jsonc
{ "ExcludedLibraries": ["Examples", "*_Tests"] }
```

Names match the first segment of a class id, case-insensitively, and `*` is a wildcard. See
[settings-reference.md](settings-reference.md#excluding-whole-libraries-from-the-checks).

**Recommended starting set for a first trial:** the self-contained documentation/ordering rules above.
Add these with care:

- **`ValidateModelReferences`** and **`ClassHasIcon`** — both need the libraries you depend on to be
  loaded. Pass `--dependency /path/to/ModelicaStandardLibrary` (repeatable) so `modelica://` links and
  icons inherited from `Modelica.Icons.*` resolve. Without it these report findings your code did not
  earn — on ExternData, 96 of them. Dependencies are loaded for resolution only and are never reported
  on. Use the **same** `--dependency` set for `baseline` as for `check`, or the two disagree about what
  resolves; the check warns when they differ. If the copy you point at is **not** the version the
  library's `uses(...)` declares, the run **stops with exit 2** rather than report findings that are
  not real — so pin the dependency checkouts in CI to the versions your libraries target. Exit 2 is
  worth handling separately in your pipeline: it means the invocation is wrong, not the code.
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

`.mlqt/baseline.json` is a reviewable ledger — one entry per finding *identity*, with its rule id,
model, and message. Its size shrinking over time is your debt burndown. (`baseline create` refuses to
overwrite an existing file; use `baseline update` to regenerate or `--force`.)

**Expect the entry count to be lower than the check's finding count**, and the later check to report
*more* accepted than the baseline holds:

```
Wrote 101032 entries to .mlqt/baseline.json, covering 104447 finding(s).
note: 3415 finding(s) share an entry with another. …
```

An entry is rule + class + element + detail with no line number, so a rule firing twice on the same
element of the same class is one entry covering two findings — and a check still reports and accepts
both. Severity is irrelevant to what gets recorded; only
[parse diagnostics](#parse-errors-always-fail) are excluded. See
[cli.md → Entries vs findings](cli.md#entries-vs-findings).

---

## 5. Gate on new findings

This is the command CI runs:

```bash
mlqt check /path/to/MyLibrary --baseline .mlqt/baseline.json --fail-on warning
```

Right after baselining, everything is accepted debt, so this **passes (exit 0)**. Introduce a new
finding (e.g. add an undescribed public parameter) and it **fails (exit 1)** — only the new finding
is reported as `new`.

Exit codes: **0** = passed, **1** = findings at/above `--fail-on`, **2** = usage/load error.

`--fail-on` controls the threshold: `warning` (fail on any new finding), `error` (fail only on new
findings from rules you set to `Error`), or `off` (never fail — report only). Start with `warning` for
the ratchet.

---

## 6. (Optional) Escalate debt in changed models

To also push cleanup of pre-existing findings in models a change touched, diff against a ref:

```bash
mlqt check /path/to/MyLibrary --baseline .mlqt/baseline.json \
      --changed-from main --touched-debt fail --fail-on warning
```

`--changed-from <ref>` marks findings in models changed since `<ref>` as **touched debt**; with
`--touched-debt fail` those also fail the gate (default `warn` reports them without failing, and
`ignore` leaves them out of the report altogether). Works with Git and SVN — run it from inside the
working copy.

**If your library is one big `package.mo`**, every model in it counts as changed whenever you touch
the file, so all its baselined debt turns into touched debt and buries the findings your change
actually introduced. Use `--touched-debt ignore` — new findings are still reported and still gate,
and you still get the "Fixed in changed models" credit for debt you cleared:

```bash
mlqt check /path/to/MyLibrary --baseline .mlqt/baseline.json \
      --changed-from main --touched-debt ignore --fail-on warning
```

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

With `--changed-from`, the report also lists findings you've **fixed** — baseline findings in changed
models that are no longer present — as positive feedback, so you can see progress even while findings
remain. Fixed findings appear in a "Fixed in changed models" section, in a `fixed` count in the
console/markdown summary, in the JSON `fixed` array, and as a `mlqt.findings.fixed` TeamCity
statistic (another burndown line).

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

Confirmed end to end on 2026-09-03: a report of 34 findings uploaded to a public repository was
accepted (`processing_status: complete`, no errors) and rendered as alerts carrying each rule's
description, help body and category. Two things to get right, both of which fail quietly:

- **The repository must be public**, or have a GitHub Code Security licence. A private repository
  without one answers `403 Code scanning is not enabled for this repository` however the token is
  scoped, and the advice to "enable code scanning in the repository settings" cannot be followed
  without the licence.
- **The `commit_sha` must be a commit GitHub has.** Name a SHA that has not been pushed and the
  upload is accepted, processing completes, and nothing appears — indistinguishable from success.

Uploading by hand, without the CodeQL action (`gh` reports what the API says, which is the point):

```powershell
$bytes = [IO.File]::ReadAllBytes('mlqt.sarif')
$ms = New-Object IO.MemoryStream
$gz = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionMode]::Compress)
$gz.Write($bytes, 0, $bytes.Length); $gz.Close()

gh api --method POST /repos/OWNER/REPO/code-scanning/sarifs `
  -f commit_sha=$(git rev-parse HEAD) -f ref=refs/heads/main `
  -f sarif=$([Convert]::ToBase64String($ms.ToArray()))

# the answer is here, not in the 202 above
gh api /repos/OWNER/REPO/code-scanning/sarifs/<id>
```

The `sarif` field is gzipped **and then** base64-encoded, and `gh` does not do that for you. A token
needs the `security_events` scope (`gh auth refresh -h github.com -s security_events`); a normal
login does not include it.

- **With code scanning / Actions:** emit SARIF and upload it, so findings appear as PR annotations:
  ```bash
  mlqt check ./MyLibrary --baseline .mlqt/baseline.json --fail-on warning \
        --format sarif --sarif-base .. --out mlqt.sarif
  ```
  `--sarif-base` names the directory the paths in the report are relative to, and GitHub resolves
  them against the root of the checkout. With the library in a subdirectory — `./MyLibrary` here —
  leaving it out writes `Model.mo` where GitHub needs `MyLibrary/Model.mo`, and every annotation
  silently attaches to nothing. Like `--config` and `--baseline`, a relative path is resolved
  **against the library**, so `..` is the repository root here; in a workflow,
  `--sarif-base "$GITHUB_WORKSPACE"` says it outright whatever the depth.
  Accepted debt is left out of the SARIF automatically: GitHub has no way to show a result as
  accepted, so uploading the baseline would fill the Security tab with alerts nobody is expected to
  act on. The run says how many it omitted, and `--sarif-include-accepted` overrides it. Note also
  that GitHub rejects more than 25,000 results in one upload and shows the first 5,000 — MLQT warns
  at both thresholds, which is worth knowing before wondering why a big library's alerts never
  appeared.
- **One run, both:** the SARIF for annotations and a markdown summary for the PR comment come from
  a single check — `--report` writes an extra format to a file alongside the primary output:
  ```bash
  mlqt check ./MyLibrary --baseline .mlqt/baseline.json --fail-on warning \
        --format sarif --sarif-base .. --out mlqt.sarif --report markdown:mlqt.md
  ```
- **Without Actions:** emit a markdown summary and post it as a PR comment from your runner/script:
  ```bash
  mlqt check ./MyLibrary --baseline .mlqt/baseline.json --fail-on warning \
        --format markdown --out mlqt.md
  ```
  The exit code still gates; the markdown gives reviewers a readable summary.

---

## 8. Comment on the pull request

Alerts in a Security tab are read by whoever goes looking. A review comment on the line itself is read
by the person who wrote the line, while they are still looking at it. `--format review` writes the
body of a GitHub pull-request review — a summary plus inline comments — and `gh` posts it:

```bash
mlqt check ./MyLibrary --changed-from origin/main       --baseline .mlqt/baseline.json --fail-on warning       --format review --out review.json

gh api --method POST /repos/OWNER/REPO/pulls/$PR/reviews --input review.json
```

MLQT writes the payload and stops there: no token, no HTTP, nothing to maintain when the API moves.
It needs `--changed-from`, naming the pull request's base branch.

This is the path for a repository that **cannot use code scanning** — a private repository without a
GitHub Code Security licence answers 403 to the SARIF upload (see [section 7](#7-wire-it-into-ci)),
and a review comment needs nothing but a token that can write to the pull request.

Three things worth knowing before you wire it up, each of which fails quietly or loudly in its own way:

- **A comment must be on a line in the diff.** GitHub rejects a review containing even one comment
  outside it — and rejects the *whole* review, not that comment. So findings on lines this change did
  not touch go in the summary body instead. Nothing is dropped; it is just not inline.
- **The checkout needs history.** The diff is measured from the merge base of the base ref and `HEAD`,
  which a shallow clone does not have: `actions/checkout` needs `fetch-depth: 0`. Without it the run
  stops with exit `2` rather than posting an empty review.
- **The review never requests changes.** It is always a comment; the exit code is what fails the
  build. Full detail in [the CLI reference](cli.md#commenting-on-a-pull-request).

Accepted debt is never commented on. Touched debt is, marked *(pre-existing)* — so the boy-scout rule
from [section 6](#6-optional-escalate-debt-in-changed-models) shows up where the change is being read.

---

## 9. Catch it before the commit

CI is the gate that counts, because it is the one nobody can skip. It is also the slowest place to
learn that a description is missing: by then the change is pushed, a build has run, and the fix is a
second commit. `mlqt hook` installs the same check as a git `pre-commit` hook, using the settings and
baseline you have just set up:

```bash
mlqt hook install ./MyLibrary --fail-on warning --baseline .mlqt/baseline.json
```

The hook skips any commit that stages no `.mo` file, so it costs nothing on the others. It blocks a
commit whose findings reach `--fail-on`, and also one where the check could not run at all — a check
that did not run has approved nothing. `git commit --no-verify` bypasses it, deliberately: a hook that
cannot be got past is a hook that gets deleted. Full options in
[the CLI reference](cli.md#running-the-check-before-each-commit).

Two things to be clear about:

- **It does not replace the CI gate.** `.git/hooks` is not committed, so every person installs their
  own and anyone can skip one. The hook shortens the feedback loop; CI is what holds the line.
- **It is git-only.** SVN's pre-commit hooks run on the server and would need MLQT installed there.
  For SVN, the desktop app checks before its commit dialog.

If your repository already runs hooks through husky, pre-commit or lefthook (anything that sets
`core.hooksPath`), add the `mlqt check` line to that configuration instead — git reads hooks only
from the configured directory, so an installed one would sit unused.

---

## 10. Maintaining the baseline

Both maintenance commands drop entries whose findings you have fixed. The difference is whether they
can also **add**:

| | Drops fixed entries | Accepts new findings as debt |
|---|---|---|
| `mlqt baseline prune <root>` | yes | **no** — it can only ever shrink the baseline |
| `mlqt baseline update <root>` | yes | **yes**, and only with `--force` |

- **When you fix findings:** run `prune`. It banks the progress and cannot silently accept debt someone
  just added, so it is safe to run at any time or on a schedule. Commit the result. If findings exist
  that it left alone, it tells you how many still fail the gate.
- **After deliberately accepting more debt** (e.g. you enabled new rules, or imported a large library):
  `mlqt baseline update <root> --force`. Without `--force` it refuses and tells you how many findings
  it would have absorbed — accepting a finding nobody reviewed is the one way to defeat the ratchet
  by accident. Review the resulting diff; the baseline growing should be visible in code review.
- **Never run `update --force` from CI.** That turns the gate off one commit at a time. CI should only
  ever *read* the baseline.
- **When you change which rules are enabled**, the next check warns that the baseline was generated
  with a different rule set, and names what changed. Findings from a newly enabled rule are reported
  as new until you accept them with `update --force` (or fix them). See
  [cli.md](cli.md#rule-drift).

---

## Command & flag reference

```
mlqt check <library-path> [--baseline <path>] [--changed-from <ref>]
                          [--touched-debt warn|fail|ignore]
                          [--config <path>]
                          [--format console|json|junit|sarif|teamcity|markdown|review]
        # --format review: a GitHub pull-request review body; needs --changed-from and Git
                          [--out <file>] [--fail-on off|warning|error] [--no-color]
                          [--no-suppress] [--dependency <path>]
                          [--metrics] [--metrics-out <path>] [--metrics-force]

mlqt baseline create|prune|update <library-path> [--baseline <path>] [--config <path>]
                                                 [--dependency <path>] [--force]
        # --force: create = overwrite an existing file; update = accept new findings as debt
        # --dependency: repeatable; must match between baseline and check

mlqt hook install|uninstall|status [<library-path>] [--fail-on off|warning|error]
                                   [--baseline <path>] [--changed-from <ref>]
                                   [--dependency <path>] [--force]
        # installs the check as a git pre-commit hook; the library defaults to the current directory
        # --force: replace or delete a pre-commit hook mlqt did not write
```

`--config` defaults to `<library-path>/.mlqt/settings.json`; `--baseline` for the `baseline` commands
defaults to `<library-path>/.mlqt/baseline.json`. A **relative** `--config`/`--baseline` value is
resolved against the library/repository path (not the current directory), so
`--baseline .mlqt/baseline.json` finds `<repo>/.mlqt/baseline.json` from any working directory;
absolute paths are used as-is — the same applies to `--metrics-out`.

The `baseline` file records the time, revision and branch it was generated at (`version: 2`); a
version-1 file written before that still loads.

---

## Tracking the trend from CI

`mlqt check --metrics` appends a point to `<root>/.mlqt/metrics-history.json` — the file the desktop
app's **Metrics** tab plots ([metrics-dashboard.md](metrics-dashboard.md)). Add it to the CI job and
the burndown builds itself, one point per
commit that actually moved the numbers, each stamped with its revision and branch.

```bash
mlqt check /path/to/MyLibrary --baseline .mlqt/baseline.json --fail-on warning --metrics
```

Recording happens whatever the gate decides — a failing build is exactly the one you want on the chart.

### The commit loop, and why it doesn't happen

If CI commits the updated history file to share it, that commit triggers CI, which updates the file
again… forever. `--metrics` prevents this by **not writing a point that says nothing new**:

- a point already exists for this **revision** → skip (covers rebuilds and retries);
- the numbers are **identical to the previous point** → skip.

The build triggered by CI's own commit measures the same library, gets the same numbers, writes
nothing and commits nothing. The cycle ends after one extra run — without relying on your CI system's
path filters or `[skip ci]` handling being configured correctly.

A commit step that is safe to drop into a default-branch job:

```bash
mlqt check "$PWD" --baseline .mlqt/baseline.json --fail-on warning --metrics || GATE=$?

if ! git diff --quiet -- .mlqt/metrics-history.json; then
  git add .mlqt/metrics-history.json
  git commit -m "Update MLQT coverage history [skip ci]"
  git push
fi

exit "${GATE:-0}"
```

Note the `git diff --quiet` guard: because an unchanged run leaves the file byte-identical, most builds
commit nothing at all.

### Or don't commit it

If you would rather not have CI push to the repository, keep the history outside it:

```bash
mlqt check "$PWD" --metrics-out "$CI_ARTIFACT_DIR/metrics-history.json"
```

No commits, so no loop is possible. The trade-off is that the trend lives in CI rather than in the
desktop app, and you have to restore the previous file into each run for it to accumulate.

**Which to choose:** commit it if you want reviewers to see the burndown in MLQT; keep it as an
artifact if your CI must not push. Committing from the **default-branch job only** is the usual middle
ground — the trend follows the mainline instead of gaining a point per PR.

---

## Parse errors always fail

Two findings sit outside the ratchet entirely:

| Rule id | Meaning |
|---------|---------|
| `MLQT.Parse.SyntaxError` | The file has a syntax error. The parser recovered, so the class still loaded — but part of it was misread. |
| `MLQT.Parse.Failure` | The file could not be parsed at all. No classes were extracted from it and nothing in it was checked. |

They are **always reported** (no setting enables or disables them), **always errors** (so they fail
even the default `--fail-on error`), **cannot be suppressed** with a `__MLQT` annotation, and are
**never written to a baseline** — so `baseline create` cannot accept one and the ratchet cannot
tolerate one.

This is on purpose. Every style rule reads a parse tree; when part of a file did not parse, those
rules quietly report on the code they *could* read and stay silent about the rest. A gate that
tolerated a parse error would be reporting a clean bill of health on code it never looked at.

The fix is always the same: correct the syntax. A common cause is a missing closing quote in a
`Documentation(info="…")` annotation, which makes the string run to the end of the file — MLQT
reports that as *"Unterminated string literal — no closing `"` before the end of the file"*, with the
line where the string starts.

The same diagnostics appear in the desktop app's Findings panel and from the MCP server, with identical
wording and line numbers.

---

## Suppressing intentional findings

Some findings are deliberate — most notably a **declaration order that matters** for a Modelica
tool's nonlinear-system heuristics. Waive a rule in the source with a **`__MLQT` vendor annotation**
(these survive reformatting, unlike comments, and are ignored by Dymola/OpenModelica):

```modelica
model Foo
  parameter Real R "Resistance" annotation(__MLQT(suppress="Naming.Convention"));   // one component
  // ...
  annotation(__MLQT(suppress="Doc.ClassDescription", reason="Legacy public API"));  // the whole class
end Foo;
```

- `suppress` is a comma-separated list of rule ids; **`*`** waives all rules. A rule id may be written
  in full (`MLQT.Naming.Convention`) or short (`Naming.Convention`).
- Class-level annotations waive the rule for the whole class; component-level annotations only for
  that component.
- **`preserveOrder=true`** (or `format=false`) on a class waives the ordering/formatting rules —
  the in-source way to mark an order-sensitive class. Include a `reason`.
- **`spelling`** is a comma-separated list of words the class accepts as spelled correctly, e.g.
  `annotation(__MLQT(spelling="Stodola,Pacejka"));`. It waives only the spelling findings for those
  words in that class, so the rest of the class is still spell checked. The desktop app writes one
  when you choose **Ignore** on a misspelled word. For a term the whole repository uses, put it in
  `.mlqt/dictionary.txt` instead.
- Suppressed findings are **never emitted** — they don't appear in reports, don't enter the baseline,
  and don't gate. This is different from the baseline: a suppression is a *permanent, intentional*
  waiver; the baseline is *temporary debt*.
- Run `mlqt check … --no-suppress` to **audit** — it ignores the annotations and reports everything.

## Gating on coverage, not just findings

The baseline gate stops *new findings*. It says nothing about whether the library is getting better,
which is the question behind a documentation push. The coverage numbers answer that, and they gate:

```bash
# nothing may go backwards — the ratchet, applied to coverage
mlqt check ./MyLibrary --coverage-ratchet --metrics

# and once a team has picked a bar, hold it there
mlqt check ./MyLibrary --min-coverage 80 --min-coverage class-description=95
```

`--coverage-ratchet` compares each dimension against the last snapshot in
`.mlqt/metrics-history.json`; with `--metrics` on the same run, every build records the point the
next one is measured against. This is the coverage counterpart of the baseline: a legacy library
adopts it on day one, because it demands no particular number — only that the number stops falling.

A coverage gate is independent of `--fail-on`, and either failing exits `1`. Both write their reason
to stderr, so a failed build says which dimension and by how much:

```
error: coverage gate: Class description 75% is below the required 80%
```

## Known limitations

- **A trimmed package is reported at its declaration.** Every report names the line in the file,
  including SARIF annotations for a class nested deep in a `package.mo`. The one exception is a
  finding about a *package* class: MLQT stores a package's source without the child classes that have
  their own files, so a line inside it is no longer the file's line. Rather than point confidently at
  the wrong line, such a finding is reported at the package's own declaration. Findings about the
  classes themselves — the overwhelming majority — carry exact file lines.
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
| Touched debt swamps the report | A single-file library marks every model changed on any edit. Use `--touched-debt ignore`. |
| `--metrics` wrote nothing | Expected when the numbers haven't moved, or the revision already has a point. The note on stderr says which. Use `--metrics-force` to override (never in a job that commits the file). |
| The metrics chart is empty in the app | The dashboard reads `.mlqt/metrics-history.json`; make sure CI's commits of it are being pulled, or press **Save snapshot** once locally. |
| Gate fails on `MLQT.Parse.SyntaxError` and the baseline doesn't help | It is not meant to. Fix the syntax error — see [Parse errors always fail](#parse-errors-always-fail). |
| `baseline create` recorded fewer than the check found | Expected. It writes one entry per finding *identity*, and a rule can fire several times on the same element of one class. Both numbers are on the `create` line, and the note explains the gap. See [cli.md → Entries vs findings](cli.md#entries-vs-findings). |
| A baselined check accepts *more* findings than the baseline holds | Also expected, and the same cause: classification is per finding, so several findings can match one entry. |
| `error: baseline not found` | The `--baseline` path is wrong, or you haven't run `baseline create` yet. |
| `error: dependency version mismatch` | The `--dependency` checkout is not the version the library's `uses(...)` declares. Point it at the right version, update the annotation, or pass `--allow-version-mismatch` if the difference is deliberate. |
| Gate passes but you expected a failure | Findings default to `Warning`; use `--fail-on warning`, or set the rule to `Error` in `RuleSeverities` and use `--fail-on error`. |
