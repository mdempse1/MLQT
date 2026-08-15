# Design Note — Phase 6: Wave-1 analyses + metrics dashboard

> **Status: PLANNED.** Phase 6 of the locked roadmap ([roadmap.md](roadmap.md) §2 Wave 1, §3
> dashboard). Builds on the findings foundation ([design-phase1-findings-foundation.md](design-phase1-findings-foundation.md)),
> the baseline/ratchet ([design-phase3-baseline.md](design-phase3-baseline.md)), and suppression
> ([design-phase5-suppression.md](design-phase5-suppression.md)) — all shipped. This is the phase
> that generates the *debt-ledger content* which makes the ratchet compelling: six new static
> analyses plus the metrics/burndown surface that visualises the debt coming down.

## Purpose

Ship the six **Wave-1** analyses — the ones that need only the user's own source and therefore
**cannot produce library-visibility false positives** — and the **metrics dashboard** that reviews
progress against them:

1. **Unused-element detection** — parameters, constants, imports, protected/local components.
2. **Unused-class detection** — nested/protected classes never referenced.
3. **Duplicate / shadowing declarations** — same name twice, duplicate import aliases, inherited-member shadowing.
4. **`uses` annotation hygiene** — libraries referenced-but-undeclared and declared-but-unused.
5. **`package.order` / file-structure consistency** — entries vs actual classes/files, missing/stale entries, standalone-vs-nested placement.
6. **Missing-units presence check** — `Real`-derived variables/parameters with no `unit` attribute (**presence only, never dimensional analysis**).

Plus a **metrics dashboard** tab (LOC, component/connection counts, inheritance depth, and
**coverage %** for descriptions/docs/units/icons) — the burndown/review surface — and, as the last
increment, **coverage trend** over time.

Every analysis emits the **same `Finding`** type as the existing style rules, so it inherits the
whole machinery for free: rule-id + severity, reformat-stable fingerprint (baseline/ratchet),
`__MLQT` suppression, and every output format (console/JSON/JUnit/SARIF/TeamCity/markdown) across
the desktop app, the `mlqt check` CLI, and the MCP server. **No new output plumbing.**

**Non-goals** (explicitly later): the confidence-aware resolver and any resolution-dependent
(Wave-2) check — broken references, connection integrity, deprecated-API, cyclic deps
(roadmap §2 Wave 2, Phase 8); complexity/clone metrics (roadmap §3); dimensional analysis (the
flagship ⚠ item). Phase 6 stays inside the no-resolution-risk boundary.

## Current state (from a full pipeline map)

- **Rules are per-class visitors, hand-called.** `StyleChecking.RunStyleCheckingFindings`
  ([StyleChecking.cs:46](../ModelicaGraph/StyleChecking.cs#L46)) parses *one* class and runs an
  explicit `if (settings.X) { var v = new XVisitor(basePackage); v.VisitStored_definition(tree);
  findings.AddRange(v.Findings); }` block per rule. Each visitor extends
  `VisitorWithModelNameTracking` ([VisitorWithModelNameTracking.cs:12](../ModelicaParser/StyleRules/VisitorWithModelNameTracking.cs#L12))
  and emits via `AddViolation(line, message, ruleId, elementPath?, discriminator?)` — `ModelId` is
  injected from `CurrentModelName`. Severity is stamped afterward from `settings.SeverityFor(id)`
  (`StyleChecking.cs:162`); suppressions are filtered last via `MlqtSuppressionExtractor` on the
  same tree (`:171`).
- **Invocation is per-`ModelNode`, in parallel.** `LibraryCheckSession.Check` → `StyleCheckRunner.RunFindings`
  runs once per model; a visitor sees **only that class's parse tree** and has **no graph access**.
  Cross-model inputs (`KnownModelIds`, a spell checker, a `BaseClassHasIcon` callback) are computed
  once in `StyleCheckContext.Build` and threaded into the specific visitor constructors that need them.
- **The rule registry** is `RuleIds` (const strings) + `RuleCatalog.BuildBuiltIn()` (id → title,
  category, default severity, description). A new rule id absent from `StyleCheckingSettings.RuleSeverities`
  is **disabled by default**. Enablement is a bool facade over the severity map, mirrored by a
  hand-written `MudSwitch` in `SettingsRepositories.razor` and a field in that file's change-detection.
- **Inheritance-aware resolution already exists but lives in the wrong assembly.**
  `ClassElementResolver.Collect` ([McpServer/Helpers/ClassElementResolver.cs:34](../MLQT.McpServer/Helpers/ClassElementResolver.cs#L34))
  merges `extends` bases, implements derived-shadows-inherited, records `InheritedFrom`, and is
  diamond/cycle-guarded; `TypeResolver.Resolve`/`ResolveWithInheritance` resolve type specifiers via
  imports/aliases/wildcards. **Both are under `MLQT.McpServer`, not a shared lib** — a
  `// Phase 2 will unify this with a shared, span-aware resolver` note already flags the intent.
- **Declaration enumeration is solved.** `ClassInterfaceExtractor.ExtractFromClass`
  ([ClassInterfaceExtractor.cs:28](../ModelicaParser/Visitors/ClassInterfaceExtractor.cs#L28)) returns
  a flat, source-ordered `ClassElement` list classified `Component | Extends | Import | Class` with
  `Name`, `Type`, `Variability`, `Causality`, `IsPublic`, `Prefixes`, `DefaultValue`, `Description`,
  `Line`. This is the reuse target for analyses 1, 3, 6.
- **Graph facts already tracked:** reverse edges `DirectedGraph.GetModelUsedBy` / `ModelNode.UsedByModelIds`;
  forward `GetUsedModels` / `UsedModelIds`; `IsNested`/`ParentModelName`/`ClassType`;
  `CanBeStoredStandalone` (= no `replaceable/redeclare/inner/outer` prefix); `PackageOrder` (read from
  `package.order`) vs `NestedChildrenOrder`/`GetModelsInFile` (actual children); `Uses` (parsed
  `uses(...)` map); `LibraryId`; `StartLine`/`StopLine`; `HasCustomIcon`. **Written** package.order via
  `ModelicaPackageSaver.BuildPackageOrderList`.
- **No metrics aggregator, no charts, no routed pages.** The app is single-page: every "page" is a
  `MudTabPanel` in `MainLayout.razor:298-311`. No `MudChart` usage anywhere; MudBlazor's chart types
  are available but unused. Nothing persists historical metrics.

## The one architectural decision: two analysis shapes

The six analyses do **not** all fit the per-class visitor model. They split cleanly:

| Shape | Sees | Analyses |
|-------|------|----------|
| **Per-class** (existing pattern) | one class's parse tree | 1 unused-element, 3a duplicate-in-class, 6 missing-units |
| **Graph-level** (new seam) | the whole `DirectedGraph` | 2 unused-class, 3b inherited shadowing, 4 `uses` hygiene, 5 `package.order` |

Per-class analyses drop straight into `RunStyleCheckingFindings` as new `if`-blocks. The graph-level
ones cannot — they need cross-model edges, inheritance chains, and filesystem layout. **Phase 6a
introduces a graph-analyzer seam** so both shapes produce the same `Finding` stream and share
severity/fingerprint/suppression uniformly:

```csharp
// ModelicaGraph/Analysis/IGraphAnalyzer.cs
public interface IGraphAnalyzer
{
    string[] RuleIds { get; }                                   // for the "any enabled?" gate
    IEnumerable<Finding> Analyze(GraphAnalysisContext ctx);     // Findings attributed to ModelIds
}
// GraphAnalysisContext: DirectedGraph, StyleCheckingSettings, (optional) library roots + file layout.
```

A `GraphAnalysisRunner` iterates the registered analyzers once per check run, collects their
`Finding`s, stamps severity, and applies suppression **per target model** (reusing
`MlqtSuppressionExtractor` on each referenced `ModelId`'s source — the `SuppressionSet` is keyed by
`ModelId`, so a `__MLQT(suppress=…)` on the flagged class works identically). It runs in the shared
checking layer (`MLQT.Services/Checking/`) so **the GUI, CLI, and MCP all get graph analyses with no
per-surface work** — same as the per-class rules today.

**Suppression, cleaned up (recommended):** rather than duplicate the per-model suppression walk in
two places, lift suppression *out* of `RunStyleCheckingFindings` into the session orchestrator, so
one pass filters both per-class and graph findings for a given model. This is a small, well-tested
refactor (Phase 5's `SuppressionSet`/`IsSuppressed` are unchanged) and removes a latent
inconsistency. *Alternative:* leave per-class suppression where it is and add a second walk in the
graph runner — simpler diff, minor duplication.

**Resolver promotion (6a prerequisite):** move `TypeResolver` + `ClassElementResolver` from
`MLQT.McpServer/Helpers/` into a shared home (`ModelicaGraph/Analysis/`, alongside the new seam), so
analyses 2/3b/6 (inheritance, type-to-predefined-base) can use them and MCP keeps working via a
re-exported reference. Guard with the existing McpServer resolver tests (they must stay green).

**Dependency-analysis prerequisite:** graph analyses 2 and 4 need `UsedByModelIds`/`UsedModelIds`
populated, i.e. `GraphBuilder.AnalyzeDependenciesAsync` must have run. The GUI already does this in
the pipeline; the **CLI/MCP check path must ensure dependency analysis runs before graph analyzers**
(it is the expensive step — note the cost, and gate graph analyzers off "is any of their rules
enabled?" so a pure style-check run doesn't pay for it).

## Per-analysis design

### 1 — Unused-element  *(per-class visitor; `MLQT.Unused.*`)*

Collect declared names via `ClassInterfaceExtractor` (already gives `IsPublic`, `Variability`,
`Line`), then set-difference against **every identifier actually referenced** in the class's
equations, algorithms, modifications, and other declarations' bindings. **The gap:** no
intra-class usage collector exists — the existing reference visitors only record refs that resolve
to *other loaded classes* and discard local identifiers. Build a small `modelicaBaseVisitor` that
harvests every `component_reference` leaf `IDENT` (and the head of a dotted ref) across those
subtrees; a name declared but never in that set is unused.

**False-positive discipline (this is what keeps it Wave-1-trustworthy):**
- **Public components of a model are interface, not dead code** — they may be driven by a `connect`
  in a parent or *are* an output. Do **not** flag public components; scope confident flagging to
  **protected components, local `parameter`/`constant`, and imports**.
- **A member of an extended class may be used only by a subclass.** Guard with the graph: if the
  class has inheritors (`UsedByModelIds` via `extends`), demote or skip member-unused there. Leaf
  classes (no inheritors) are high-confidence. (This is the one per-class analysis that reads a
  single graph fact — pass "is this class extended?" through `StyleCheckContext` like `BaseClassHasIcon`.)
- Rule ids: `MLQT.Unused.Parameter`, `MLQT.Unused.Constant`, `MLQT.Unused.Component` (protected/local),
  `MLQT.Unused.Import`. Default **Warning**; element path = the name.

### 2 — Unused-class  *(graph analyzer; `MLQT.Unused.Class`)*

A `ModelNode` with empty `UsedByModelIds` is a candidate. Policy by visibility/scope (matches
roadmap): **nested/protected class, zero usedBy → Warning** (high confidence — nothing in the
readable universe can reference it); **unreferenced public top-level class → Info** ("possibly unused
API" — a consumer we can't see may use it, never gate). Public/protected is not on `ModelNode`; read
it by running `ClassInterfaceExtractor` on the *parent* and checking the nested element's `IsPublic`.
**Mitigate the resolver's blind spot** (`UsedByModelIds` misses `extends`-inherited and non-type
uses): treat zero-usedBy as *not-proven-used*, and before flagging, cross-check the element resolver
(#3b) so a class referenced only through an inherited name isn't a false positive.

### 3 — Duplicate / shadowing  *(3a per-class; 3b graph; `MLQT.Duplicate.*`, `MLQT.Shadowing.*`)*

- **3a Same-name-twice / duplicate import alias** — pure per-class: group `ClassInterface.Elements`
  by name (and imports by alias); a repeat is a real bug. `MLQT.Duplicate.Declaration` (**Error** —
  this is a correctness defect, not a style nit), `MLQT.Duplicate.Import` (**Warning**).
- **3b Inherited-member shadowing** — needs the extends chain: `ClassElementResolver.Collect`
  already computes `InheritedFrom` and shadow order, so a derived member silently masking an
  inherited one is a report over data it already produces. `MLQT.Shadowing.InheritedMember`
  (**Warning**; medium confidence — legal in Modelica, often intentional, so warn not error).

### 4 — `uses` hygiene  *(graph/library analyzer; `MLQT.Structure.Uses*`)*

`uses(...)` is already parsed into `ModelNode.Uses` on the root package. Build the **referenced-library
set**: for every class in a library, map each `UsedModelIds` entry to its owning library (via
`LibraryId`, fallback to the root name segment), union them, then diff against the root package's
`Uses.Keys`: **used-but-undeclared** (`MLQT.Structure.UsesUndeclared`) and **declared-but-unused**
(`MLQT.Structure.UsesDeclared­Unused`), both **Warning**, attributed to the root package `ModelNode`.
This is library-maintainer gold and doubles as the "expected-external namespace" allowlist the Wave-2
resolver will consume. (No missing-library risk: it only reasons about what the user's own source
references, never whether an external target exists.)

### 5 — `package.order` consistency  *(graph + filesystem analyzer; `MLQT.Structure.PackageOrder`)*

Both sides already exist: the declared `ModelNode.PackageOrder` array vs the ground truth
(`NestedChildrenOrder` / `GetModelsInFile` + on-disk sibling `.mo` files and subdirectories).
`ModelicaPackageSaver.BuildPackageOrderList` is the reference for what a *correct* order contains
(incl. its import/extends exclusions). Report: entries with no matching class/file, classes/files
missing from the order, order mismatches, and standalone-vs-nested placement mistakes
(`CanBeStoredStandalone` gives the rule). `MLQT.Structure.PackageOrder`, **Warning**, element =
package model id + a `Discriminator` naming the offending entry (so multiple issues in one package
fingerprint distinctly). Pure comparison logic; no new parsing.

### 6 — Missing-units  *(per-class visitor + type resolution; `MLQT.Units.MissingUnit`)*

For each `Real`-derived variable/parameter with no `unit` attribute, flag it (**presence only**).
Two sub-cases: a direct `Real x` with no `unit=` in its modification is trivial (walk
`class_modification → argument → element_modification.name()` for `"unit"` — copy the
`ModelAnalyzer` external-annotation walk). SI types (`type Length = Real(unit="m")`) require resolving
the declared type down to its predefined base and checking whether `unit` is set anywhere along the
chain — build an "ultimate predefined base + effective attributes" helper on the promoted
`TypeResolver`. **Warning; medium confidence** — derived numeric types often set `unit` themselves,
so absence at the declaration site can be intentional; treat a type whose chain already fixes `unit`
as covered. Skip `Integer`/`Boolean`/`String`/`enumeration`. Cheap, huge burndown content.

## Metrics dashboard  *(Phase 6e)*

A new **tab** (the app is single-page): `MLQT.Shared/Pages/MetricsDashboard.razor` rendered as a
`MudTabPanel` in `MainLayout.razor:298-311` (icon `BarChart`). Backed by a new singleton
**`IMetricsAnalysisService`** in `MLQT.Services` (interface in `Interfaces/`), registered in **both**
composition roots (`MLQT/MauiProgram.cs` and `MLQT.McpServer/Program.cs`) — the established
`IImpactAnalysisService` pattern.

**What it computes** (one aggregation pass over `CombinedGraph` / `GetAllModels()` + `ModelNode`
fields):
- **Size/shape:** class count, LOC (from `StopLine − StartLine` per model — no extra parsing),
  component counts, connection counts (from `connect` edges / `BehaviorExtractor`), max/avg
  inheritance depth (walk `extends`), fan-in/fan-out (`UsedBy`/`Used`).
- **Coverage %** = `1 − failures / eligible`, per dimension: description, documentation info,
  documentation revisions, icon, unit. **The denominator is not in the finding set** (findings count
  only *failures* of *enabled* rules over *checked* models), so the service runs its **own dedicated
  pass** over all models/components rather than scraping `ICodeReviewService.LogMessages` — robust,
  always-available, independent of which rules are enabled or whether style checking has run in
  deferred mode. Icon coverage reads `ModelNode.HasCustomIcon` directly.

**Presentation:** MudBlazor `MudChart` (bar/donut) for per-dimension coverage and category
breakdowns — available, currently unused, no new dependency. Grouping of findings by
`RuleDefinition.Category` gives the issue-mix view (the `Category` seam Phase 1 built for exactly this).

**Pipeline wiring:** add a deferred-aware metrics step to `MainLayout.RunStartUpAsync` (after external
resources) and `OnProjectChanged`, mirroring the existing deferred pattern — new `AppState`
`HasMetricsComputed` flag + `OnRunDeferredMetrics` event + `RunDeferredMetricsAsync()`/`MetricsComputed()`
marker, subscribed in `MainLayout.OnInitializedAsync`. On big libraries it defers like the other analyses.

**Headless reuse:** because the service lives in `MLQT.Services` and needs only the graph, expose it
as an MCP `get_metrics` tool and a CLI `mlqt metrics` summary (or a `--metrics` addition to the check
report) with zero GUI dependency — same three-surface reuse the checks get.

## Coverage trend / burndown  *(Phase 6f — last, optional)*

The dashboard's payoff is watching coverage climb. Trend needs **persisted snapshots** — nothing
stores history today. Add a lightweight snapshot writer (`.mlqt/metrics-history.json`: timestamp +
the coverage/size numbers) written on demand or piggy-backed on a baseline update, rendered with
MudBlazor's `MudTimeSeriesChart`. Kept last and separable because it introduces persistence + a
history file the others don't need; the roadmap rates trend ⭐⭐/L versus the dashboard ⭐⭐⭐/M.

## Rule inventory (registry seed)

New category segment `Unused`/`Duplicate`/`Shadowing`/`Structure`/`Units` in the id; `RuleCatalog`
`Category` groups them for the dashboard. All **disabled by default** (absent from the severity map,
like every rule) — a library opts in per `.mlqt/settings.json`.

| Rule ID | Analysis | Shape | Category | Default | Element | Confidence |
|---------|----------|-------|----------|---------|---------|-----------|
| `MLQT.Unused.Parameter` | 1 | per-class | Unused | Warning | component | high (leaf) |
| `MLQT.Unused.Constant` | 1 | per-class | Unused | Warning | component | high (leaf) |
| `MLQT.Unused.Component` | 1 | per-class | Unused | Warning | component | high (protected/local) |
| `MLQT.Unused.Import` | 1 | per-class | Unused | Warning | import | high |
| `MLQT.Unused.Class` | 2 | graph | Unused | Warning / **Info** (public top-level) | class | high / low |
| `MLQT.Duplicate.Declaration` | 3a | per-class | Correctness | **Error** | component | high |
| `MLQT.Duplicate.Import` | 3a | per-class | Correctness | Warning | import | high |
| `MLQT.Shadowing.InheritedMember` | 3b | graph | Correctness | Warning | component | medium |
| `MLQT.Structure.UsesUndeclared` | 4 | graph | Structure | Warning | package | high |
| `MLQT.Structure.UsesDeclaredUnused` | 4 | graph | Structure | Warning | package | high |
| `MLQT.Structure.PackageOrder` | 5 | graph+fs | Structure | Warning | package + entry discriminator | high |
| `MLQT.Units.MissingUnit` | 6 | per-class+resolve | Units | Warning | component | medium |

## Settings UI

Adding twelve rules as hand-written `MudSwitch`es — each needing its own switch *and* a field in
`StyleSettingsChanged` (miss the latter and persistence/re-check silently break) — is the sprawl this
phase must avoid. **Decision: this phase replaces the hand-written rule toggles with a data-driven
list generated from `RuleCatalog.BuiltIn` grouped by `Category`, and does it *before* the analyses
land** so the twelve new rules cost zero `.razor` edits rather than twelve throwaway switches (see
sub-phase 6b). One loop renders the toggles; adding a rule later needs no UI change. This is the
per-rule-severity editor foreshadowed in roadmap §4 (and deferred from Phase 4), so the same control
grows from an on/off switch to an Off/Warn/Error selector over the `RuleSeverities` map.

Scope to get right when migrating:
- **Change-detection** moves from field-by-field bool comparison to comparing the `RuleSeverities`
  map (and the handful of non-rule formatter flags that stay as their own switches).
- **Coupled / non-toggle rules** must be handled: `ExtendsAtTop` has no independent switch (it rides
  `ImportStatementsFirst`), and `ComponentsBeforeClasses` / `ApplyFormattingRules` are formatter flags,
  not catalog rules — the data-driven list must render only independently-toggleable rules and leave
  those where they are.
- **Existing rule toggles migrate too**, so the settings page isn't a mix of data-driven and
  hand-written switches. This is the main reason it's a distinct sub-phase rather than a footnote.

## Ordered work breakdown (each sub-phase compiles + tests green)

- **6a — Substrate.** Promote `TypeResolver`/`ClassElementResolver` into `ModelicaGraph/Analysis/`
  (McpServer references the new home; resolver tests stay green). Add `IGraphAnalyzer` +
  `GraphAnalysisRunner`, wire into `LibraryCheckSession` so graph findings merge with per-class
  findings (severity + fingerprint + suppression uniform); centralise suppression at the session
  (recommended) or add the per-model walk in the runner. Ensure the CLI/MCP check path runs
  dependency analysis when a graph rule is enabled. New `RuleIds`/`RuleCatalog` category scaffolding.
  *No user-visible rule yet — pure seam, tested with a trivial stub analyzer.*
- **6b — Data-driven rule settings UI.** Replace the hand-written rule switches in
  `SettingsRepositories.razor` with a list generated from `RuleCatalog.BuiltIn` grouped by `Category`;
  move change-detection to a `RuleSeverities`-map comparison; keep the formatter flags
  (`ComponentsBeforeClasses`, `ApplyFormattingRules`) and coupled rules (`ExtendsAtTop`) handled
  correctly. Done *before* the analyses so their twelve rule ids surface automatically. No new rules
  here — a refactor that must leave the current toggles behaving identically (guard with a
  settings round-trip test).
- **6c — Per-class analyses.** Unused-element (with the leaf-class guard via `StyleCheckContext`),
  duplicate-declaration/-import, missing-units (Real + SI resolution). Per the recipe (now without the
  `.razor` step, thanks to 6b); `ModelicaParser` visitors must keep the assembly **>95%** covered.
- **6d — Graph analyses.** Unused-class, inherited-member shadowing, `uses` hygiene, `package.order`
  consistency — each an `IGraphAnalyzer`. `ModelicaGraph` **>80%**.
- **6e — Metrics dashboard.** `IMetricsAnalysisService` + `MetricsDashboard.razor` tab + coverage
  dedicated pass + `MudChart`; deferred-mode `AppState` wiring; MCP `get_metrics` + CLI metrics reuse.
- **6f — Coverage trend.** Snapshot persistence + `MudTimeSeriesChart`. Optional/last.

Recommended order is 6a → 6b → 6c/6d (either order; 6c is lower-risk, ship first for early burndown) →
6e → 6f. 6b lands before the analyses so their rules appear in the data-driven list rather than as
throwaway switches. 6e depends only on 6a's graph access, not on the analyses, so it can proceed in
parallel once the substrate lands.

## Tests

- **Per-class visitors:** table-driven positive/negative cases (unused vs used-in-equation /
  used-in-modifier / used-only-by-subclass; duplicate vs distinct; `Real` no-unit vs `Real(unit=)`
  vs SI-type vs `Integer`). Snapshot the message strings. Keep `ModelicaParser` >95%.
- **Graph analyzers:** small synthetic graphs — nested-unused vs referenced; public-top-level → Info;
  shadowing via `extends`; `uses` declared/undeclared/used/unused permutations; `package.order`
  missing/stale/misordered/standalone-misplaced. Assert `ModelId`/`ElementPath`/severity/discriminator.
- **Suppression parity:** a `__MLQT(suppress="MLQT.Unused.Class")` on a flagged class hides a
  *graph* finding exactly as it hides a per-class one (guards the 6a suppression path).
- **Fingerprint stability:** a graph finding's fingerprint is invariant under reformat and file
  moves (baseline safety) — same guarantees Phase 1 established, now over graph findings.
- **CLI/MCP integration:** the new rules appear in every formatter and honour `--fail-on`/baseline;
  a graph rule enabled alone triggers dependency analysis and gates correctly.
- **Metrics:** coverage math (numerator/denominator) on a fixture with known gaps; LOC/counts;
  deferred-mode not-yet-computed guard.

## Key decisions & risks

- **Decision — two analysis shapes, one `Finding` stream.** The graph-analyzer seam is the central
  new abstraction; everything downstream (severity, fingerprint, suppression, all output formats,
  baseline) is reused unchanged. This is what keeps six analyses from becoming six integrations.
- **Decision — Wave-1 = self-contained w.r.t. *external* libraries.** All six reason only about the
  user's own source, so none can produce library-visibility false positives (the roadmap's trust
  gate). The residual FP risk is *internal* resolver blind spots (`extends`-inherited names) —
  mitigated by conservative policy (Info for low-confidence cases, leaf-class guards, resolver
  cross-checks), **not** by resolving into external namespaces (that's Wave-2).
- **Risk — resolver promotion.** Moving `TypeResolver`/`ClassElementResolver` out of `McpServer`
  touches a load-bearing, well-tested helper. Contain it to a move + namespace change with the MCP
  resolver tests as the guardrail; do it first (6a) in isolation.
- **Risk — dependency-analysis cost in headless runs.** Graph analyses require it; it's the expensive
  step. Gate off "is any graph rule enabled?" so style-only runs don't pay, and document the cost.
- **Risk — unused-element false positives** on extended classes and public interface components —
  the single most trust-sensitive analysis. Ship it *conservative* (protected/local/leaf only,
  public components excluded); widen only with evidence. Better under-reporting than a flood.
- **Decision — settings-UI sprawl → data-driven list (sub-phase 6b, committed).** Rather than add
  twelve manual switches + change-detection fields, the rule toggles become a `RuleCatalog`-driven
  list, landed before the analyses so their rules cost no UI work. The migration risk is contained to
  `SettingsRepositories.razor` + its change-detection, guarded by a settings round-trip test; coupled
  rules (`ExtendsAtTop`) and formatter flags stay handled as today.
- **Decision — coverage via a dedicated pass, not finding-scraping.** Robust denominator,
  independent of enablement/deferred state; trend persistence is separated into 6f because it alone
  needs a history file.
