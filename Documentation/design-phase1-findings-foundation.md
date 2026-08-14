# Design Note — Phase 1: Findings Foundation

> **Status: design / not yet implemented.** Phase 1 of the locked roadmap
> ([roadmap.md](roadmap.md)). Prerequisite for the CI quality gate
> ([design-ci-quality-gate.md](design-ci-quality-gate.md)) and every later analysis phase.

## Purpose

Introduce a rich, structured **`Finding`** model — carrying a stable **rule ID**, a
**severity enum**, structured **element identity**, and a **reformat-stable fingerprint** —
to replace the identity-free `LogMessage` that style rules emit today. This is the data
foundation the whole roadmap keys on: the baseline/ratchet (fingerprint), the CI gate
(severity), suppression (rule ID + element), custom rules (rule registry), SARIF/JUnit
output (serializable record), and the dashboard (category aggregation).

**Non-goals for Phase 1** (later phases, but seams are built now): the CLI, baseline
persistence, `__MLQT` suppression *implementation*, per-rule severity *editing UI*, the new
analyses, the dashboard. Phase 1 must be **behaviour-preserving** — the GUI Code Review page
and the MCP tools produce byte-identical output.

## Current state (from a full pipeline map)

- **One flat type, `LogMessage`** ([LogMessage.cs](../ModelicaParser/DataTypes/LogMessage.cs))
  carries every issue (style, parser, external tool): `ModelName` (FQN — the correlation key),
  `Summary`, `Details`, `Severity` (a free *string*, always `"Style warning"` for style rules),
  `LineNumber`, `Source` (`"StyleChecking"`/`"Parser"`/`"ExternalTool"`). No rule ID, no element
  field, no severity enum.
- **Rules report via `AddViolation(line, message)`** in
  [VisitorWithModelNameTracking.cs:46](../ModelicaParser/StyleRules/VisitorWithModelNameTracking.cs#L46),
  accumulating into `RuleViolations`. The FQN is available (`CurrentModelName`); the element
  name is a local (`variableName`/`name`/`className`) but is only interpolated into `Summary`.
- **Orchestration** is a single static `StyleChecking.RunStyleChecking(...)`
  ([StyleChecking.cs:28](../ModelicaGraph/StyleChecking.cs#L28)) with one
  `if (settings.<Boolean>)` block per rule group; returns `List<LogMessage>`.
- **Only two callers** consume that output: `StyleCheckingWorker` (raises the
  `OnViolationsFound(List<LogMessage>)` event the GUI stores) and the MCP `StyleCheckRunner`
  (mapped to DTOs in `StyleTools`). That is the entire projection surface.

### Load-bearing string contracts (must preserve or migrate in lockstep)

1. Spelling `Summary` prefix `"Misspelled word '<w>'…"` — GUI `IsSpellingViolation` /
   `ExtractMisspelledWord` regex and dictionary-removal ([CodeReview.razor](../MLQT.Shared/Pages/CodeReview.razor)).
2. Exact literals `"…in documentation info"` / `"…in documentation revisions"` /
   `"…in description"` — GUI dictionary-removal matches these verbatim.
3. `Source == "StyleChecking"` — service clearing ([StyleCheckingService.cs:38](../MLQT.Services/StyleCheckingService.cs#L38))
   and MCP defaulting key off it.
4. `Severity` free string — MCP `list_issues` does substring filtering on it.
5. GUI table reads `ModelName`, `Summary`, `LineNumber`, `Severity`; details dialog opens
   only when `Details` is non-empty (so style findings must keep `Details == ""`).

**Consequence:** Phase 1 keeps the visitor *message strings unchanged* and keeps the
`Finding → LogMessage` projection emitting `Severity="Style warning"`, `Source="StyleChecking"`,
`Details=""`. The new structured data rides *alongside* on `Finding`, consumed only by new
code. Zero consumer behaviour change.

## Target data model

New types in `ModelicaParser` (the >95%-coverage assembly; keep `LogMessage`):

```csharp
// ModelicaParser/DataTypes/Finding.cs
public sealed record Finding
{
    public required string RuleId { get; init; }        // stable, e.g. "MLQT.Naming.Convention"
    public required string ModelId { get; init; }       // FQN, from CurrentModelName
    public string? ElementPath { get; init; }           // "R" (component), null for class-level
    public string? Discriminator { get; init; }         // multi-instance disambiguator (misspelled word)
    public required string Message { get; init; }        // unchanged human text (preserves contracts)
    public int LineNumber { get; init; }                 // DISPLAY ONLY — never in the fingerprint
    public RuleSeverity Severity { get; init; } = RuleSeverity.Warning; // stamped by orchestrator

    public string Fingerprint => FindingFingerprint.Compute(RuleId, ModelId, ElementPath, Discriminator);

    public LogMessage ToLogMessage() => new(ModelId, "Style warning", LineNumber, Message)
        { Source = "StyleChecking" };   // byte-identical to today's output
}

// ModelicaParser/StyleRules/RuleSeverity.cs
public enum RuleSeverity { Off, Info, Warning, Error }   // Off == disabled
```

- It is a **record** so later phases add fields (e.g. a Wave-2 `Confidence`) with a default,
  non-breakingly. Do **not** add speculative fields now (YAGNI).
- `Severity` is stamped by the orchestrator from the settings map, not by the visitor — visitors
  stay ignorant of configuration (important for custom rules).

### The rule registry (the extensibility seam)

```csharp
// ModelicaParser/StyleRules/RuleCatalog.cs
public sealed record RuleDefinition(
    string Id, string Title, string Category, RuleSeverity DefaultSeverity, string Description);

public static class RuleCatalog
{
    // built-in rules registered here; custom rules (Phase 9) register at runtime
    public static IReadOnlyDictionary<string, RuleDefinition> BuiltIn { get; }
    public static RuleSeverity DefaultSeverityFor(string ruleId);
}

// ModelicaParser/StyleRules/RuleIds.cs — string constants, one per rule id below
public static class RuleIds { public const string ImportStatementsFirst = "MLQT.Style.ImportStatementsFirst"; /* … */ }
```

Consumed by: the severity map (defaults + validation), suppression matching (Phase 5), SARIF
`rules[]` metadata (Phase 4), the dashboard's category grouping (Phase 6), and custom-rule
registration (Phase 9).

## Fingerprint specification

```
Fingerprint = hex( SHA256( RuleId ‖ 0x00 ‖ ModelId ‖ 0x00 ‖ (ElementPath ?? "") ‖ 0x00 ‖ (Discriminator ?? "") ) )[..32]
```

- **Line number is deliberately excluded** — survives edits elsewhere in the file.
- **Uses a fixed hash (SHA-256), never `string.GetHashCode()`** — `GetHashCode` is randomised
  per process, which would silently invalidate every baseline across restarts. This is the
  single most important correctness detail in Phase 1.
- Robust to reformatting and to a standalone class moving between `package.mo` and its own file,
  because it is built from semantic identity (rule + FQN + element), not source position.
- **`ElementPath` scheme:** the component/element identifier within the class (`"R"`), or `null`
  for class-level rules. Component names are unique within a class, so name alone disambiguates.
- **`Discriminator`:** only for rules that can fire multiple times on one element — spelling uses
  the misspelled word (already a local in the visitor). If the word changes, it reads as
  fixed+new, which is correct.

Built and unit-tested in Phase 1 though nothing consumes it until the Phase 3 baseline.

## Settings model change — dictionary-backed, bool facades

Change `StyleCheckingSettings` ([StyleCheckingSettings.cs](../ModelicaGraph/StyleCheckingSettings.cs))
so a **`Dictionary<string, RuleSeverity>` is the source of truth**, and the existing ~16 named
`bool` properties become **computed facades** over it:

```csharp
public Dictionary<string, RuleSeverity> RuleSeverities { get; set; } = new();

public bool ImportStatementsFirst
{
    get => Get(RuleIds.ImportStatementsFirst) != RuleSeverity.Off;
    set => Set(RuleIds.ImportStatementsFirst, value);   // Off, or the registry default severity
}
// Get(id) => RuleSeverities.TryGetValue(id, …) ?? RuleCatalog.DefaultSeverityFor(id)
// Set(id,on) => RuleSeverities[id] = on ? RuleCatalog.DefaultSeverityFor(id) : RuleSeverity.Off
```

Why this option (vs. a full dictionary cut, or bool+parallel-map):

- The map is what **every later phase keys on** (baseline, suppression, gate, custom rules), so it
  should be authoritative from day one — avoids a second migration.
- **Zero churn** to the settings UI ([SettingsRepositories.razor](../MLQT.Shared/Components/SettingsRepositories.razor))
  and the MCP `StyleSettingsInput` DTO in Phase 1 — both keep binding to the bool facades. The
  per-rule **Off/Warn/Error** selector UI is deferred to Phase 4, editing the map directly then.

**Migration / persistence** (`.mlqt/settings.json`, load/save at
[RepositoryService.cs:239](../MLQT.Services/RepositoryService.cs#L239) / `:567`):

- **Old files (bools only):** deserialization calls each bool facade setter → populates the map.
  Seamless upgrade, no explicit converter needed for the common case.
- **New files:** serialize the `RuleSeverities` map as authoritative *and* keep writing the bools
  for one or two releases (older MLQT builds still read enablement). On load, if an explicit map is
  present it wins; if absent, the bool-derived entries stand.
- The fiddly bit — making "map wins when both present" deterministic regardless of JSON property
  order — is handled in an `[OnDeserialized]` reconciliation (track whether the map key appeared),
  with a dedicated round-trip test. Flag this as the one non-trivial migration detail.
- `HasAnyStyleRuleEnabled` becomes "any rule id resolves to severity != Off".

## Producer refactor

1. **Base visitor** — `RuleViolations` becomes `List<Finding>`; extend the reporting primitive:
   ```csharp
   protected void AddViolation(int line, string message, string ruleId,
                               string? elementPath = null, string? discriminator = null);
   ```
   `Finding.ModelId` = `CurrentModelName`; `Severity` left at default here (stamped later).
2. **Each rule visitor** passes its `ruleId` (from `RuleIds`) and, where it already has one, the
   `elementPath`/`discriminator` (mechanical — the locals already exist; see
   `PublicParametersAndConstantsHaveDescription.cs:142`, `FollowNamingConvention.cs:178`,
   `SpellCheckDescriptions.cs:102`). Multi-rule visitors (`CheckClassAnnotations` →
   Info/Revisions/Icon) pass the specific sub-rule id per call.
3. **`RunStyleChecking`** returns `List<Finding>`; replace each `if (settings.<Boolean>)` with an
   "is this rule id active (severity != Off)?" check; after collecting, **stamp `Severity`** on each
   finding from the settings map; route the list through an injectable **`IFindingSuppressor`**
   (Phase-1 no-op passthrough — the seam for Phase 5 `__MLQT` suppression, which will key on the
   `RuleId`+`ElementPath` this phase introduces).

## Consumer preservation

- **StyleCheckingWorker** and **MCP StyleCheckRunner** receive `List<Finding>` and call
  `.ToLogMessage()` to feed the existing `List<LogMessage>` event / DTO paths — output unchanged.
- **Optional early win (additive, non-breaking):** enrich the MCP `StyleViolationDto` / `IssueItem`
  with `RuleId` (and reuse `Finding` for the existing `Category`). MCP is the ideal first real
  consumer of structured findings and it costs almost nothing.
- The GUI, `CodeReviewService`, `StyleCheckingService` events, and settings UI are **untouched** in
  Phase 1.

## Built-in rule inventory (registry seed)

| Rule ID | Visitor | Category | Default | Element path |
|---------|---------|----------|---------|--------------|
| `MLQT.Doc.ParameterDescription` | PublicParametersAndConstantsHaveDescription | Documentation | Warning | component |
| `MLQT.Doc.ConstantDescription` | ″ | Documentation | Warning | component |
| `MLQT.Style.ImportStatementsFirst` | ImportStatementsFirst | Ordering | Warning | class |
| `MLQT.Style.ExtendsAtTop` | ExtendsClausesAtTop | Ordering | Warning | class |
| `MLQT.Style.InitialEqAlgoFirst` | InitialEquationFirst | Ordering | Warning | class |
| `MLQT.Style.InitialEqAlgoLast` | InitialEquationFirst | Ordering | Warning | class |
| `MLQT.Style.OneOfEachSection` | OneOfEachSection | Ordering | Warning | class |
| `MLQT.Style.DontMixEquationAndAlgorithm` | OneOfEachSection | Ordering | Warning | class |
| `MLQT.Style.DontMixConnections` | MixConnectionsAndEquations | Ordering | Warning | class |
| `MLQT.Doc.ClassDescription` | CheckClassDescriptionStrings | Documentation | Warning | class |
| `MLQT.Doc.ClassDocumentationInfo` | CheckClassAnnotations | Documentation | Warning | class |
| `MLQT.Doc.ClassDocumentationRevisions` | CheckClassAnnotations | Documentation | Warning | class |
| `MLQT.Doc.ClassIcon` | CheckClassAnnotations | Documentation | Warning | class |
| `MLQT.Reference.ModelReferences` | CheckModelReferences | Reference | Warning | element (URI) |
| `MLQT.Spelling.Description` | SpellCheckDescriptions | Spelling | Warning | class + word discriminator |
| `MLQT.Spelling.Documentation` | SpellCheckDocumentation | Spelling | Warning | class + word discriminator |
| `MLQT.Naming.Convention` | FollowNamingConvention | Naming | Warning | class or component |

Notes: all default to `Warning` to reproduce today's single `"Style warning"` level.
`FollowNamingConvention` stays one rule id in Phase 1 (matches its single toggle); splitting into
Class/Parameter/Constant/Variable sub-ids is a later option. `ComponentsBeforeClasses` is a **dead
setting** (no visitor) — not registered. `ApplyFormattingRules` is a formatter flag, not a check.

## Roadmap seams established in Phase 1

| Seam | Serves | Phase |
|------|--------|-------|
| Fingerprint (built + tested, unused) | baseline/ratchet | 3 |
| Rule registry (extensible) | suppression match, SARIF metadata, custom rules | 4/5/9 |
| Severity map (authoritative store) | CI gate, per-rule severity UI | 4 |
| `IFindingSuppressor` no-op seam | `__MLQT` suppression | 5 |
| `Finding` as clean serializable record | JUnit/SARIF/JSON emit | 2 |
| `ElementPath` structured identity | analyses attach to elements; suppression granularity | 5/6 |
| Category on rule defs | dashboard grouping | 6 |
| Record shape (easy field addition) | Wave-2 `Confidence` | 8 |

## Ordered work breakdown (each step compiles + tests green)

1. Add `Finding`, `RuleSeverity`, `FindingFingerprint`, `RuleIds`, `RuleDefinition`, `RuleCatalog`
   (all additive; nothing consumes them yet). Unit-test fingerprint stability/robustness.
2. Base visitor: `RuleViolations` → `List<Finding>`; new `AddViolation` overload. Update every rule
   visitor to pass its rule id + element identity. (Broad but mechanical; existing tests guard message text.)
3. `RunStyleChecking` → `List<Finding>`; map-driven activation; severity stamping; no-op suppressor seam.
4. Add `IFindingSuppressor` (+ no-op default) and wire it.
5. `StyleCheckingSettings`: `RuleSeverities` map + bool facades + `[OnDeserialized]` migration;
   `HasAnyStyleRuleEnabled` via the map.
6. Update the two consumers (Worker, StyleCheckRunner) to project via `ToLogMessage()`; optional MCP
   DTO `RuleId` enrichment.
7. Tests (see below); confirm ModelicaParser coverage stays >95%.

## Tests

- **Fingerprint:** deterministic across processes (guards against `GetHashCode`); invariant under
  line shifts and reformatting; distinct per (rule, model, element); discriminator behaviour for
  multi-instance spelling.
- **Registry completeness:** every rule id emitted by a visitor exists in `RuleCatalog`; no orphan ids.
- **Settings migration:** old bool-only JSON loads to correct map; new map-bearing JSON round-trips;
  map-wins-when-both-present; bool facades reflect map.
- **Projection parity:** `Finding.ToLogMessage()` reproduces today's `Summary`/`Severity`/`Source`/
  `Details`/`LineNumber` exactly — snapshot the load-bearing spelling and doc-info/revisions strings.
- **Behaviour:** existing StyleChecking/StyleCheckingService/MCP tests pass unchanged.

## Key decisions & risks

- **Decision:** dictionary-backed settings with bool facades (recommended) — clean end-state model,
  minimal Phase-1 blast radius. *If preferred, a full dictionary cut is possible but forces the
  settings-UI regeneration into Phase 1.*
- **Decision:** new `Finding` record + keep `LogMessage`; project at the two consumer sites.
- **Risk:** touching every rule visitor — broad but mechanical; message text is unchanged and
  guarded by existing tests.
- **Risk:** the settings migration reconciliation (map vs bools, property order) — contained to
  `StyleCheckingSettings` with a dedicated test.
- **Risk:** ModelicaParser >95% coverage bar — plan fingerprint/registry tests up front.
