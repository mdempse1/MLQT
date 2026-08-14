# Design Note — CI Quality Gate & Baseline / Ratchet

> **Status: design / under discussion.** This is a forward-looking design note, not
> user documentation for a shipped feature. See [roadmap.md](roadmap.md) §1, §4, §5.

## Motivation

Modelica teams have spent years building large libraries that almost certainly contain
thousands of pre-existing style/analysis issues. No team will fix them all in one go
just to get a green CI check. For MLQT's quality checks to be adoptable in CI, the gate
must:

1. Run headlessly under any CI runner (first customer: **TeamCity**; prospect: **GitHub**,
   no Actions yet). → generic CLI + standard report formats, no platform-specific
   integration required first.
2. **Not** fail on the existing debt (baseline).
3. Fail on **genuinely new** issues introduced by a change.
4. Optionally nudge developers to clean up **pre-existing** issues **in models they
   touched** (the "boy-scout rule").

## Classification model — two orthogonal axes

Every finding carries two independent booleans:

- **`inBaseline`** — was this exact issue already recorded as accepted debt?
- **`inChangedModel`** — is it in a model whose own source text changed in this commit/PR?

| | Model unchanged | Model changed in this commit |
|---|---|---|
| **Not in baseline (new)** | **FAIL** — regression | **FAIL** — regression |
| **In baseline (existing debt)** | pass (accepted debt) | **warn by default** (configurable → fail for strict teams) |

**Key correctness point:** newness and changed-ness are *independent*. A new issue can
appear in a model whose own text never changed (e.g. editing a base class makes an
inherited-annotation or naming check fire on a derived model). Therefore:

- "New" is defined *purely* by baseline membership — never by "is in a changed file."
- "Changed model" is a separate escalation axis layered on top.

This means the gate only needs to analyze **one revision** (the current checkout) plus a
**cheap VCS diff** to know which models changed — no re-analysis of the base branch.

**Decision (2026-07-17):** touched-debt escalation **warns by default**; strict teams
opt into fail.

## Stable finding identity (the crux)

A baseline is only usable if each finding has a fingerprint that survives edits that
aren't the issue itself — line shifts, **and a full reformat** (MLQT reformats code), and
a standalone class moving between `package.mo` and its own file.

MLQT's advantage over textual linters (ESLint, Checkstyle) is that it has a parse tree
with **stable semantic names**, so identity is semantic, not textual:

```
fingerprint = hash(ruleId + fullyQualifiedClassName + elementPath [+ discriminator])
```

- `ruleId` — stable rule id, e.g. `MLQT.Naming.Parameter`
- `fullyQualifiedClassName` — e.g. `MyLib.Components.Resistor`
- `elementPath` — the parameter/connector/section/annotation the finding is about
  (e.g. `param:R`, `connector:p`, `class`)
- `discriminator` — only for rules that fire multiple times on one element
  (e.g. two spelling errors in one description → the offending word)

Robust to line shifts, reformatting, and file relocation. The only tradeoff: for
multi-instance rules keyed on content that changes when fixed (a misspelled word),
fixing reads as one "fixed" + zero "new" — which is correct.

## Baseline = a version-controlled debt ledger

Store the baseline as a checked-in file: **`.mlqt/baseline.json`** (instance-level, one
fingerprint per known finding). Chosen over base-branch recomputation because it is:

- **Reviewable** — baseline growth shows up in a PR diff (visible, auditable debt).
- **Deterministic & offline** — identical locally and in CI, Git or SVN, no second revision fetch.
- **A debt ledger** — `count by rule` over the baseline *is* the technical-debt report;
  watching it shrink is a reporting/morale win.

Lifecycle:

- `mlqt baseline create` — snapshot all current findings (the one-time "accept existing
  debt" action for a legacy library).
- `mlqt baseline update` — regenerate; a PR that adds net-new entries is visible and can
  itself be gated ("baseline may not grow without approval").
- **Stale-entry pruning** — baseline entries whose fingerprint no longer appears = fixed
  issues; report and offer to prune. Never fail on these.

Ratchet property: the baseline is a **ceiling that only moves down** by default.
Base-branch comparison can exist later as an optional mode for teams who don't want a
checked-in file.

## Changed-model detection

Goes through the existing `IRevisionControlSystem` abstraction so it works for Git **and**
SVN:

- **PR/MR builds** — diff against the merge base (Git) / branch point (SVN).
- **Push builds** — diff against the previous commit / target-branch revision.
- Map changed `.mo` paths → changed model FQNs (file→model mapping already in the graph).
- "Changed" = the model's own defining text changed. Do **not** transitively mark
  dependents changed — the baseline already catches genuinely-new inherited findings via
  the newness axis.

## CLI surface (MVP)

```
mlqt check <library-path> \
  --baseline .mlqt/baseline.json \
  --changed-from <ref>            # or --changed-files <list> from the CI diff
  --format junit --out results.xml \
  --fail-on new                  # new | new+touched | none
  --severity-threshold warning
# exit 0 = gate passed, non-zero = failed

mlqt baseline create|update|prune <library-path> [--baseline .mlqt/baseline.json]
```

## Output formats — integration without integrations

Priority order (all are serializers over one findings model):

| Format | Purpose | Priority |
|--------|---------|----------|
| **Exit code** | The gate itself — minimal need for any runner | MVP |
| **Console/text** | Humans locally + CI logs | MVP |
| **JUnit XML** | Universal: each finding = a "test case"; native test-report UI in TeamCity/Jenkins/GitLab/Azure/GitHub | MVP |
| **SARIF** | GitHub code scanning (post-Actions), Azure DevOps | Phase 4 |
| **TeamCity service messages** | `##teamcity[buildProblem]` + `buildStatisticValue` → **auto-graph baseline debt trend** | Phase 4 |
| **Markdown summary** | PR comment body via a plain script (GitHub without Actions) | Phase 4 |
| **JSON** | Custom scripting | Phase 4 |

## Policy / configuration

Config extends the **existing** per-repo `<repo>/.mlqt/settings.json` (already
revision-controlled; read/written by `RepositoryService`). No new config file.

The central change is replacing `StyleCheckingSettings`' named on/off booleans with a
**rule-id-keyed severity map** so built-in and custom rules are uniform:

```jsonc
{
  "rules": {
    "MLQT.Style.ImportStatementsFirst": "error",
    "MLQT.Naming.Parameter": "warning",
    "Acme.NoDeprecatedResistor": "off"
  },
  "gate": {
    "failOn": "new",              // new (default) | new+touched | none
    "touchedDebtPolicy": "warn"   // warn (default) | fail | ignore
  }
}
```

- Severity ∈ `off | warning | error`. **CI reads severity directly**: warnings report but
  don't fail; errors fail. `--fail-on error` is the default gate.
- Boolean migration: existing `true` → the rule's default severity, `false` → `off`.
- Custom rules (declarative or compiled plugin) register a `ruleId` + default severity and
  slot into the same map — no schema change.

## In-source suppression — `__MLQT` vendor annotations, not comments

Some rule violations are intentional and permanent — most notably **declaration order**,
which can matter because Modelica tools' heuristics turn different orderings into
more/less complex nonlinear systems. Authors need a way to say "this rule does not apply
here, on purpose."

**Decision: use `__MLQT` vendor annotations, not comments.** Rationale, backed by the code:

- MLQT **reformats** code. Comments in the grammar are parser-tree elements bound to
  *"the element they precede in source order"* (`c_comment` / `final_comment` rules;
  `WriteCommentIfProceedsThisElement` in `ModelicaRenderer`). When the formatter reorders
  declarations, a leading `// disable-next-line` re-anchors to whatever now follows it —
  it gets orphaned. Line-comment suppression is fundamentally unsafe under a reformatter.
- Annotations are **semantically bound to their element** and already round-trip reliably
  (the icon/documentation subsystems depend on this). `__VendorName` annotations are
  spec-sanctioned, non-semantic, and ignored by Dymola/OpenModelica.

Forms:

```modelica
// class-level
annotation(__MLQT(suppress="Naming.Parameter", reason="Legacy public API"));

// class-level, declaration-order case (formatter must not reorder AND rule must not flag)
annotation(__MLQT(preserveOrder=true, reason="Order affects nonlinear system structure"));

// component-level (each declaration can carry its own annotation)
parameter Real R "Resistance" annotation(__MLQT(suppress="Naming.Parameter"));
```

Two consumers read `__MLQT`: the **renderer** honors `preserveOrder` / `format=false`;
the **style checker** honors `suppress`. Granularity matches how checks are scoped
(class / component; nested classes each carry their own annotation).

**Replaces a stale-prone list:** `preserveOrder` / `format=false` is the in-source,
rename-safe successor to the name-based `FormattingExcludedModels` setting — the exclusion
travels with the model on rename/move instead of drifting out of a central list. Keep the
list for backward compatibility; steer new usage to annotations.

**Suppression ≠ baseline:** a suppression is a permanent, intentional author assertion (a
`reason` is expected); the baseline is temporary debt to be worked off. A suppressed
finding is never emitted, so it never enters the baseline and never gates.

**Ergonomics:** never hand-typed. The Code Review UI offers a "suppress this rule here
(with reason)" action that injects the annotation on the right element; the MCP server
exposes an equivalent `suppress_rule` tool. Injecting a structured annotation via the
existing edit/render path is more reliable than a human placing a comment.

## Proposed phased build plan

Each phase is independently shippable and delivers value early.

- **Phase 0 — Findings foundation (shared).** Stable rule-id registry, per-rule severity,
  semantic fingerprint. Refactor `StyleChecking` to emit findings carrying fingerprints.
  Benefits the GUI too. **Detailed plan:**
  [design-phase1-findings-foundation.md](design-phase1-findings-foundation.md).
- **Phase 1 — Headless CLI MVP.** `mlqt check` reusing the service layer; console +
  exit code + JUnit + JSON. Packaged as a `dotnet tool`. Runs on Windows/Linux/macOS —
  **this is where Linux support actually begins.**
- **Phase 2 — Baseline / ratchet.** Baseline file + `create/update/prune`; new-vs-accepted
  classification; `--fail-on new`; stale-entry reporting.
- **Phase 3 — Changed-model escalation.** VCS diff via `IRevisionControlSystem`;
  `--changed-from`; warn-by-default touched-debt.
- **Phase 4 — CI ergonomics.** SARIF, TeamCity service messages + build statistics
  (debt trend graph), markdown summary.
- **Phase 5 — Adoption polish.** Config file, per-rule severity config, docs, sample
  TeamCity + GitHub setups.

## Open questions

- **Resolved:** config extends the existing `<repo>/.mlqt/settings.json`; baseline sits
  beside it at `.mlqt/baseline.json`.
- Severity-map refactor: fully generalize `StyleCheckingSettings` to a `Dictionary<ruleId,
  severity>`, or keep named properties (typed `Severity` instead of `bool`) plus an
  extensible map for custom rules? (Phase 0 detail.)
- Baseline file granularity if it grows very large on huge libraries (per-model grouping
  to keep diffs readable?).
- Do we ever want an optional base-branch-comparison mode, or is the checked-in baseline
  sufficient long-term?
