# Design Note — Phase 5: In-source suppression (`__MLQT` annotations)

> **Status: COMPLETE (5a, 5b, 5c all implemented; all suites green).**
> Phase 5 of the locked roadmap ([roadmap.md](roadmap.md)). Turns the Phase 1 no-op
> `IFindingSuppressor` seam ([design-phase1-findings-foundation.md](design-phase1-findings-foundation.md))
> into real suppression, implementing the mechanism agreed in
> [design-ci-quality-gate.md](design-ci-quality-gate.md) ("In-source suppression").
>
> **Done:**
> - **5a** — `MlqtSuppressionExtractor` + `SuppressionSet` read `__MLQT(suppress=…)` class/component
>   directives; `RunStyleCheckingFindings` drops suppressed findings (`honorSuppressions`); the
>   placeholder `IFindingSuppressor` is removed; CLI `--no-suppress` audit mode.
> - **5b (checker side)** — `preserveOrder=true` / `format=false` at the class level suppresses the
>   ordering/formatting rules (the declaration-order case), via the extractor adding the
>   formatting-family rule ids to the class suppress set.
> - **5b (renderer side)** — `ModelicaPackageSaver.SaveLibraryToDirectoryWithResult` treats a class
>   carrying `__MLQT(format=false/preserveOrder)` as format-excluded (rendered raw), unioning it with
>   the explicit `excludedModelIds` set — the in-source successor to `FormattingExcludedModels`.
> - **5b (coverage side, added 2026-09-04 — backlog B39).** Being the successor turned out to take a
>   third place, not two. The exclusion also has to take the class off the **layout coverage
>   dimensions**, which the name list did and the annotation did not — so a class using the
>   recommended, rename-safe mechanism was silenced in the checker and still counted on the
>   dashboard, showing a gap no finding would ever name. A settings object cannot answer this: it is a
>   fact about the source. `CoverageMeasurer` reads it while it has the tree and records
>   `CoverageFacts.FormattingPreserved`; `CoverageDimensions.ForClass` — now the single narrowing,
>   where three copies of it had been written out — consults that alongside the name list. The
>   general lesson for anything else claiming to succeed `FormattingExcludedModels`: find every
>   consumer of the list, not just the ones in the formatting pipeline.
> - **5c** — `MlqtSuppressionWriter` (adds/merges the `__MLQT(suppress=…)` annotation onto a class or
>   component via parse-tree splicing); MCP `suppress_rule` tool (over the `ClassBodyEditor` path); and
>   the Code Review **Suppress** action (carries `RuleId`/`ElementPath` through `LogMessage`, writes the
>   annotation, saves via the single-file render/reload path).

## Purpose

Let an author permanently and intentionally waive a rule at a specific class or component — for
cases where a finding is deliberate (the motivating one: **declaration order that matters** for a
solver's nonlinear-system heuristics). Suppression is expressed as a **`__MLQT` vendor annotation**,
not a comment, because:

- MLQT reformats code, and comments are position-bound (`c_comment`/`WriteCommentIfProceedsThisElement`
  in `ModelicaRenderer`) — a `// disable` comment gets orphaned when declarations are reordered;
- annotations are bound to their element and provably round-trip (the icon/documentation subsystems
  depend on it), and `__VendorName` annotations are spec-sanctioned and ignored by Dymola/OpenModelica.

**Suppression ≠ baseline.** A suppression is a *permanent, intentional* author assertion (carrying a
`reason`); the baseline is *temporary debt* to work off. A suppressed finding is never emitted, so it
never enters the baseline, never gates, and doesn't appear in reports.

## Staging

- **5a — checker suppression** (`__MLQT(suppress=…)`) — the CI-relevant core; makes the seam real.
- **5b — formatter integration** (`__MLQT(preserveOrder=true)` / `format=false`) — the in-source,
  rename-safe successor to `StyleCheckingSettings.FormattingExcludedModels`; completes the
  declaration-order story.
- **5c — authoring ergonomics** — an MCP `suppress_rule` tool and a GUI "suppress here" action, so
  nobody hand-types the annotation.

## 5a — checker suppression

### Syntax

```modelica
model Foo
  parameter Real R "Resistance" annotation(__MLQT(suppress="Naming.Convention"));   // component-level
  // ...
  annotation(__MLQT(suppress="Doc.ClassDescription", reason="Legacy public API"));  // class-level
end Foo;
```

- **Class-level** `__MLQT(suppress="…")` in the class annotation → suppresses the named rule(s) for
  that whole class.
- **Component-level** `__MLQT(suppress="…")` on a component → suppresses them for that component only.
- `suppress` is a comma-separated list of rule ids; **`*`** suppresses all rules. A token matches a
  finding's `RuleId` if it equals it, equals it minus the `MLQT.` prefix (so `Naming.Convention` and
  `MLQT.Naming.Convention` both work), or is `*`.
- `reason` is optional but recommended (not used for matching; surfaced by tooling/audit).

### Reading the annotations

A new `MlqtSuppressionExtractor` visitor (in `ModelicaParser`) walks the parse tree and collects
directives, reusing the annotation-walking pattern already in `CheckClassAnnotations`
(`composition.annotation()` → `class_modification` → `argument_list` → `element_modification`, keyed
on `name() == "__MLQT"`) — and the component-declaration annotation path for component-level ones.
It produces a `SuppressionSet`: the class-level rule set, plus a per-component rule set.

### Applying it

`StyleChecking.RunStyleCheckingFindings` already routes findings through the Phase 1 suppressor seam
(`(suppressor ?? NoOpFindingSuppressor.Instance).Apply(findings)`). Phase 5 makes this real:

- extract the `SuppressionSet` from the model's `parsedCode` (available right there in
  `RunStyleCheckingFindings`), and drop any finding a directive covers — class-level directive
  matching `RuleId`, or a component directive for the finding's `ElementPath` matching `RuleId`
  (or `*`).
- **Seam evolution:** the Phase 1 `IFindingSuppressor` parameter was a placeholder (no caller ever
  passed one). Replace it with internal annotation-based suppression plus a `bool honorSuppressions
  = true` toggle, so an audit mode can disable it (see `--no-suppress` below). Matching keys on the
  `RuleId` + `ElementPath` that Phase 1 already put on every finding.
- Because the check runs per model on that model's own definition, the extractor naturally sees that
  model's (and its nested classes') annotations.

### CLI / transparency

- No new required flags — suppression is source-driven.
- Optional **`--no-suppress`** (audit): ignore `__MLQT` and report everything, so CI can see what is
  being waived.
- Optionally track and report a **suppressed count** (`N suppressed`) in the summary/JSON, for
  visibility that suppressions are in effect.

### Tests

- class-level suppress removes that rule's findings for the class; component-level for that element;
  `*`; short and full rule ids; an un-suppressed rule still fires; `reason` doesn't affect matching.
- **reformat-invariance:** reformatting a suppressed model keeps it suppressed (annotation survives
  the renderer) — the payoff over comments.
- a suppressed finding is absent from the baseline and from every report format.

## 5b — formatter integration (declaration order)

The motivating case needs two things: the formatter must **not reorder** the class, and the checker
must **not flag** the ordering rules. There is already a per-model hook for the second half:
`RunStyleCheckingFindings` takes `isExcludedFromFormatting` and skips the formatting-family rules
(imports/sections/initial-eq order) — today sourced from the name-based
`StyleCheckingSettings.FormattingExcludedModels`.

- `__MLQT(preserveOrder=true)` (or `format=false`) on a class → the extractor reports it, and
  `RunStyleCheckingFindings` sets `isExcludedFromFormatting` for that class (reusing the existing
  path), *and* `ModelicaRenderer` does not reorder it.
- This is the **in-source, rename-safe successor** to `FormattingExcludedModels` — the exclusion
  travels with the model on rename/move instead of drifting out of a central name list. Keep the
  list for backward compatibility; steer new usage to the annotation.
- Touches the formatting pipeline: `ModelicaRenderer` reads `__MLQT` from the class annotation, and
  `ModelicaPackageSaver` passes it through.

## 5c — authoring ergonomics

Injecting `annotation(__MLQT(suppress="…", reason="…"))` by hand is fiddly; both surfaces automate it
using the existing edit/render path (which round-trips annotations reliably):

- **MCP `suppress_rule` tool** — given a model (and optional component), rule id, and reason, inject
  the annotation and save (via the existing `ClassBodyEditor` / `ModelFilePersistence` helpers).
- **GUI action** — "Suppress this rule here (with reason)" in Code Review injects the annotation on
  the right element.

Both are far more reliable than a human placing text, and they're the reason the roadmap pairs
authoring with the suppression mechanism.

## Work breakdown

- **5a** — `MlqtSuppressionExtractor` + `SuppressionSet` (ModelicaParser) → wire into
  `RunStyleCheckingFindings`, evolve the seam, add `honorSuppressions` → CLI `--no-suppress` +
  optional suppressed count → tests. *(ModelicaParser + ModelicaGraph + MLQT.Cli.)*
- **5b** — extend the extractor to `preserveOrder`/`format=false`; set `isExcludedFromFormatting`
  from it in the checker; `ModelicaRenderer`/`ModelicaPackageSaver` honour it → tests.
- **5c** — MCP `suppress_rule` tool + GUI "suppress here" action → tests. *(McpServer + Shared.)*

Recommended order: **5a first** (unblocks the CI gate's intentional-exception handling), then 5b
(declaration-order/formatting), then 5c (authoring).

## Key decisions & risks

- **Decision:** `__MLQT` vendor annotation (spec-sanctioned `__VendorName`, ignored by other tools),
  not comments.
- **Decision:** rule-id matching accepts full or `MLQT.`-stripped ids, plus `*`.
- **Decision:** suppressed findings are silent by default; `--no-suppress` audit mode + an optional
  suppressed count give transparency.
- **Risk:** annotation-parsing edge cases (component-level annotations, arrays, nested
  `class_modification`) — reuse the `CheckClassAnnotations` walking pattern and test each shape.
- **Risk:** 5b touches the formatting pipeline (`ModelicaRenderer`) — guard with the existing
  renderer/`CorrectingCodeOrder` tests.
- **Interaction:** suppression composes with the baseline (Phase 3) — permanent intent vs temporary
  debt — and with the analyses (Phase 6), which will generate the intentional-exception cases this
  unblocks.
