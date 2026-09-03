# Design Note — Phase 4: CI Ergonomics (SARIF, TeamCity, Markdown, real severities)

> **Status: IMPLEMENTED** (all suites green — ModelicaParser 1426, ModelicaGraph 467, Services 527,
> MCP 240, CLI 37). Phase 4 of the locked roadmap ([roadmap.md](roadmap.md)). Builds on the Phase 3
> classified findings ([design-phase3-baseline.md](design-phase3-baseline.md)); realises the
> CI-output half of [design-ci-quality-gate.md](design-ci-quality-gate.md).
>
> **Deviations from the sketch, decided during implementation:**
> - Severity-map reconciliation needed **no `[OnDeserialized]` hook**: `RuleSeverities` is a get-only
>   dictionary marked `[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]` (STJ merges
>   into it rather than replacing), and the bool facade only seeds a rule's default severity when the
>   map has no entry — so an explicit `"Error"` is never clobbered regardless of JSON order. The enum
>   serializes as a string via `[JsonConverter(typeof(JsonStringEnumConverter<RuleSeverity>))]`.
> - SARIF top level is built from a dictionary (to emit the `$schema` key); `informationUri` omitted.
> - GUI severity editor deferred as planned — CI users set severities in `.mlqt/settings.json`.
>   **Since shipped** (2026-09-01, ahead of Phase 7): per-repository Off/Info/Warning/Error selectors
>   in `SettingsRepositories.razor`, so this deferral is closed.
>
> **Still open from this phase** (tracked in [roadmap.md](roadmap.md), "Backlog — finishing phases
> 1–6"): B2 `--sarif-base` for repo-root-relative URIs; B3 two outputs from one run, so a pipeline
> does not check the library twice. B1 (model-relative line numbers) was closed on 2026-09-03 — see
> the note below.

## Purpose

Make the CLI a first-class citizen of real CI systems, and make the gate actually gate:

- **SARIF** — GitHub code scanning, Azure DevOps, and any SARIF viewer.
- **TeamCity service messages** — native build problems + build-statistics so TeamCity graphs the
  **baseline debt trend** automatically (the first customer runs TeamCity).
- **Markdown summary** — a PR-comment-ready report (the GitHub prospect has no Actions yet).
- **Per-rule severity persistence** — the Phase 1 severity map is currently a runtime projection
  (`[JsonIgnore]`, persistence bool-based), so every finding is `Warning` and `--fail-on error`
  is effectively report-only. Phase 4 persists the map so teams can mark rules `error` — which is
  what finally makes the gate, and SARIF `level`, meaningful.

**Non-goals** (later): the desktop **GUI** severity editor (Off/Warn/Error dropdowns in
`SettingsRepositories.razor`) — CI users edit `.mlqt/settings.json`; the GUI editor is desktop UX,
closer to Phase 7. Multiple simultaneous outputs (console *and* a report file) — `--out` per format
is enough for MVP.

## Staging

The three formatters are independent and small; severity persistence is the one cross-cutting change
and should land first so every formatter (and the gate) reflects real levels.

- **4a — Per-rule severity persistence** (unlocks real `error`).
- **4b — SARIF** formatter.
- **4c — TeamCity** formatter (service messages + build statistics).
- **4d — Markdown** summary formatter.

## 4a — Per-rule severity persistence

Today `StyleCheckingSettings.RuleSeverities` is `[JsonIgnore]` and rebuilt from the bool facades on
load. Phase 4:

- **Serialize `RuleSeverities`** in `.mlqt/settings.json` as the authoritative store, keeping the
  bool facades for backward compatibility. Reconciliation (the Phase 1 open item): on load, if the
  JSON contains an explicit `ruleSeverities` map it wins; otherwise the bool-derived entries stand.
  Implement in an `[OnDeserialized]` hook (track whether the map key was present), with a round-trip
  test — the one fiddly bit.
- Result: a rule set to `error` flows through `RunStyleCheckingFindings`' severity stamping →
  `Finding.Severity` → the gate (`--fail-on error` now fails) and SARIF `level`. No formatter code
  needs to special-case it.
- Editing stays config-level for now (hand-edited or written by tooling). The bool switches in the
  desktop UI keep working (enable ⇒ the rule's default severity). GUI Off/Warn/Error editing is the
  deferred non-goal above.

## `CheckReport` carries the gate outcome (small refactor)

Formatters are pure (`report → string`), but TeamCity needs "did the gate fail?" and markdown wants
to state it. The gate decision depends on `--fail-on`/`--touched-debt`, which the report doesn't
hold. So move the `FailsGate` computation into `CheckRunner` up front and add the result to
`CheckReport` (e.g. `int GateFailureCount`). `CheckRunner`'s exit code becomes
`report.GateFailureCount > 0`. Keeps formatters pure and gives every formatter the gate result.

## 4b — SARIF (`--format sarif`)

SARIF 2.1.0. `tool.driver.rules[]` from `RuleCatalog` (id, name=Title, shortDescription=Description,
`defaultConfiguration.level` from `DefaultSeverity`); `results[]` from the findings.

| SARIF field | Source |
|-------------|--------|
| `ruleId` | `Finding.RuleId` |
| `level` | `error`/`warning`/`note` from `Finding.Severity` (Error/Warning/Info) |
| `message.text` | `Finding.Message` |
| `locations[].physicalLocation` | `artifactLocation.uri` = file **relative to the library path** (portable for GitHub); `region.startLine` = `Finding.LineNumber` |
| `baselineState` | `New`→`new`; `AcceptedDebt`/`TouchedDebt`→`unchanged` |
| `partialFingerprints["mlqt/v1"]` | `Finding.Fingerprint` (lets SARIF viewers match results across runs) |

Include all findings with `baselineState` (code-scanning UIs use it to surface only new). Emit the
`$schema` and `version` header. URIs use forward slashes, relative to the library path.

## 4c — TeamCity (`--format teamcity`)

Emits [service messages](https://www.jetbrains.com/help/teamcity/service-messages.html) on stdout:

- **Build statistics** — the headline feature (TeamCity auto-graphs custom statistics over builds):
  ```
  ##teamcity[buildStatisticValue key='mlqt.findings.new' value='N']
  ##teamcity[buildStatisticValue key='mlqt.findings.acceptedDebt' value='M']
  ##teamcity[buildStatisticValue key='mlqt.findings.touchedDebt' value='K']
  ```
  → the baseline-debt burndown, with no database.
- **Per-finding** — an inspection/message per actionable (new/touched) finding.
- **Gate** — `##teamcity[buildProblem description='…']` when `GateFailureCount > 0`, so the build is
  marked failed with a readable reason.

Needs a small escaper for TeamCity's rules (`|`→`||`, `'`→`|'`, `[`→`|[`, `]`→`|]`, newline→`|n`).

## 4d — Markdown (`--format markdown`)

A PR-comment-ready summary the GitHub prospect can post with a plain script:

```
## MLQT check — 2 new, 0 touched, 15 accepted (gate: failed)

| Severity | Rule | Model | Line | Message |
| --- | --- | --- | --- | --- |
| warning | MLQT.Doc.ParameterDescription | MyLib.Foo | 12 | Public parameter x must have a description |
```

Lists new (and touched) findings; summarises accepted debt as a count. States the gate result.

## CLI surface

Additive: three new `--format` values (`sarif`, `teamcity`, `markdown`) → new `OutputFormat` cases +
new `IFindingFormatter` implementations + switch arms in `CheckRunner`. No new options.

## Tests

- **Severity persistence**: `ruleSeverities` map round-trips; explicit map wins over bools; an
  `error`-severity rule makes `--fail-on error` exit 1; SARIF `level` reflects it.
- **SARIF**: valid JSON, parses against the 2.1.0 shape; rule metadata present; `level`/`baselineState`
  mapping; relative URIs; `partialFingerprints` = the finding fingerprint.
- **TeamCity**: statistics lines present with correct counts; `buildProblem` emitted iff the gate
  failed; escaping of `'`/`[`/`]`/newline.
- **Markdown**: header counts; table rows for actionable findings; gate result line.
- **Regression**: existing console/JSON/JUnit output unchanged.

## Work breakdown (each step compiles + tests green)

1. **4a** — serialize `RuleSeverities` + `[OnDeserialized]` reconciliation + tests; confirm an
   `error` rule fails the gate end-to-end.
2. **Refactor** — compute the gate in `CheckRunner`, add `GateFailureCount` to `CheckReport`.
3. **4b** — `SarifFindingFormatter` + `OutputFormat.Sarif` + tests.
4. **4c** — `TeamCityFindingFormatter` (+ escaper) + `OutputFormat.TeamCity` + tests.
5. **4d** — `MarkdownFindingFormatter` + `OutputFormat.Markdown` + tests.
6. Docs: `cli.md` (formats + CI recipes), CLAUDE.md if needed.

## Roadmap seams established here

| Seam | Serves | Phase |
|------|--------|-------|
| Persisted per-rule severity | GUI severity editor; stricter gates | later / 9 |
| SARIF output | GitHub code scanning / PR annotations | (now) |
| TeamCity build statistics | dashboard/burndown cross-check | 6 |
| `GateFailureCount` on the report | any output that needs the gate verdict | (now) |

## Key decisions & risks

- **Decision:** persist the severity map (config-level) in Phase 4; defer the GUI editor.
- **Decision:** SARIF includes all findings tagged with `baselineState` (not new-only) so viewers can
  dedupe/surface as they prefer.
- **Decision:** relative-to-library URIs in SARIF for portability; document that CI may need paths
  relative to the repo root (a `--sarif-base <path>` option is a possible later refinement).
- **Risk:** the `RuleSeverities` reconciliation (map vs bools, JSON property order) — the same fiddly
  case flagged in Phase 1; contained to `StyleCheckingSettings` with a dedicated test.
- **Risk:** SARIF schema conformance — validate output against the 2.1.0 shape in tests; keep the
  document minimal (driver + rules + results) rather than exhaustive.
- **Resolved (2026-09-03, backlog B1):** finding line numbers were relative to each model's
  extracted definition, so SARIF's `region.startLine` pointed at the wrong line of a `package.mo`.
  Findings still carry class-relative lines — that is what a rule can know, and what the app's code
  viewer wants — and every report now maps them through `ClassLocation`, which knows where the class
  starts in its file. A package whose stored source was trimmed cannot be mapped line-for-line and is
  reported at its declaration instead.
