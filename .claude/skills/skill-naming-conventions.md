# Naming Convention Checking Skill

This skill covers the naming convention checking system used to validate Modelica class names, variable names, parameter names, and constant names against configurable conventions. It spans the ModelicaParser, ModelicaGraph, and MLQT.Shared projects.

## Overview

The naming convention system checks that identifiers in Modelica code follow a chosen naming style. It supports granular control per class type (model, function, block, connector, etc.) and per element visibility (public vs protected) and category (variable, parameter, constant).

**Default convention** (Modelica Standard): PascalCase for all class types except functions (camelCase), camelCase for all element names. Underscore suffixes for direction indicators (e.g., `pressure_in`, `T_a`) are allowed by default.

## Architecture

```
ModelicaParser (core logic, no service dependencies)
  StyleRules/
    NamingStyle.cs              - Enum: Any, CamelCase, PascalCase, SnakeCase, UpperCase
    NamingConventionConfig.cs   - Parser-layer config record (no ModelicaGraph dependency)
    NamingValidator.cs          - Pure static validation logic, no ANTLR dependencies
    FollowNamingConvention.cs   - ANTLR visitor extending VisitorWithModelNameTracking

ModelicaGraph (orchestration + settings)
  NamingConventionSettings.cs   - Serializable settings with flat properties for JSON
  NamingConventionPresets.cs    - Factory methods for predefined presets
  StyleCheckingSettings.cs      - FollowNamingConvention bool + NamingConvention property
  StyleChecking.cs              - Wires FollowNamingConvention visitor into RunStyleChecking()

MLQT.Shared (UI)
  Components/NamingStyleSelect.razor         - Reusable MudSelect<NamingStyle> wrapper
  Components/SettingsStyleChecking.razor      - Default naming convention settings panel
  Components/SettingsRepositories.razor       - Per-repo naming convention settings panel
```

## NamingStyle Enum

**File:** `ModelicaParser/StyleRules/NamingStyle.cs`

```csharp
public enum NamingStyle { Any, CamelCase, PascalCase, SnakeCase, UpperCase }
```

- `Any` — No enforcement, all names accepted
- `CamelCase` — Starts lowercase, no underscores (e.g., `myVariable`)
- `PascalCase` — Starts uppercase, no underscores (e.g., `MyModel`)
- `SnakeCase` — All lowercase with underscores (e.g., `my_variable`)
- `UpperCase` — All uppercase with underscores (e.g., `MY_CONSTANT`)

## NamingValidator

**File:** `ModelicaParser/StyleRules/NamingValidator.cs`

Static utility with no ANTLR dependencies. All methods are pure functions.

| Method | Purpose |
|--------|---------|
| `IsValid(name, style, allowSuffixes)` | Main entry point — checks name against style with optional suffix stripping |
| `IsCamelCase(name)` | Starts lowercase, no underscores |
| `IsPascalCase(name)` | Starts uppercase, no underscores |
| `IsSnakeCase(name)` | All lowercase + underscores + digits, no leading/trailing/double underscores |
| `IsUpperCase(name)` | All uppercase + underscores + digits, no leading/trailing/double underscores |
| `IsShortAbbreviation(name)` | Letter followed by only digits (T, P3, V12) — always valid |
| `StripSuffix(name)` | Strips last `_segment`; returns `(baseName, suffix)` |

**Key behaviors of `IsValid`:**
1. `null`/empty names → always valid
2. `NamingStyle.Any` → always valid
3. Short abbreviations (letter + digits) → always valid (before and after suffix stripping)
4. Suffix stripping (when enabled): strip the last `_segment`, check base name against style

## NamingConventionConfig

**File:** `ModelicaParser/StyleRules/NamingConventionConfig.cs`

Parser-layer config record passed into the visitor. Uses a `record` type to support `with` expressions in tests.

```csharp
public record NamingConventionConfig
{
    public Dictionary<string, NamingStyle> ClassNamingRules { get; init; } = new();
    public NamingStyle PublicVariableNaming { get; init; }
    public NamingStyle PublicParameterNaming { get; init; }
    public NamingStyle PublicConstantNaming { get; init; }
    public NamingStyle ProtectedVariableNaming { get; init; }
    public NamingStyle ProtectedParameterNaming { get; init; }
    public NamingStyle ProtectedConstantNaming { get; init; }
    public bool AllowUnderscoreSuffixes { get; init; }
    public HashSet<string> ExceptionNames { get; init; } = [];
}
```

The `ClassNamingRules` dictionary is keyed by Modelica class keyword: `"model"`, `"function"`, `"block"`, `"connector"`, `"record"`, `"type"`, `"package"`, `"class"`, `"operator"`.

`ExceptionNames` contains names that bypass all convention checks (case-sensitive). Used for product names like "NASCAR" or established abbreviations like "OMC".

## FollowNamingConvention Visitor

**File:** `ModelicaParser/StyleRules/FollowNamingConvention.cs`

Extends `VisitorWithModelNameTracking`. Follows the same patterns as `PublicParametersAndConstantsHaveDescription.cs`.

### State Tracking

| Field | Purpose |
|-------|---------|
| `_isPublic` | Reset to `true` in `OnClassEntered()`, toggled in `VisitComposition()` |
| `_currentElementCategory` | Set in `VisitComponent_clause()` from `type_prefix` |
| `_classTypeStack` | Pushed in `VisitClass_definition()`, popped in `OnClassExited()` |
| `_pendingClassName/Type/Line` | Deferred class name check (see below) |

### Deferred Class Name Checking

The class name must be checked after the base class pushes the model name onto the stack (so violations report the correct model). The pattern:

1. `VisitClass_definition()` — stores class name, type, and line in `_pending*` fields, then calls `base.VisitClass_definition()` (which pushes the model name)
2. `OnClassEntered()` — reads pending fields, calls `CheckClassName()`, clears pending fields

### Class Type Extraction

From `class_prefixes` grammar rule, checked most specific first: function > connector > record > model > block > type > package > operator > class.

### Element Name Checking

In `VisitComponent_declaration()`:
1. Extract name from `declaration().IDENT().GetText()`
2. Strip surrounding single quotes from quoted identifiers (`'r_0'` → `r_0`)
3. Strip array subscripts (`name[3]` → `name`)
4. Check against exception names
5. Determine style from `_isPublic` + `_currentElementCategory`
6. Call `NamingValidator.IsValid()` with suffix settings
7. Add violation if invalid

### Violation Messages

- `"Class name 'myModel' should be PascalCase (model)"`
- `"Variable name 'MyVar' should be camelCase (public variable)"`
- `"Parameter name 'BadParam' should be camelCase (protected parameter)"`
- `"Constant name 'myConst' should be UPPER_CASE (public constant)"`

## NamingConventionSettings

**File:** `ModelicaGraph/NamingConventionSettings.cs`

Serializable settings model with flat properties for clean JSON serialization. Lives in ModelicaGraph so it can reference `NamingStyle` from ModelicaParser.

Key methods:
- `ToConfig()` — Converts flat properties to `NamingConventionConfig` dictionary for the visitor
- `Equals(NamingConventionSettings)` — Value equality for change detection in UI
- `Clone()` — Deep copy for backup/restore in settings dialogs

Properties match the `NamingConventionConfig` fields plus:
- `PresetName` — Display name of the current preset (or "Custom")
- `ExceptionNames` — `List<string>` of names that bypass all checks

## NamingConventionPresets

**File:** `ModelicaGraph/NamingConventionPresets.cs`

Three predefined presets:

| Preset | Classes | Functions | Elements | Constants |
|--------|---------|-----------|----------|-----------|
| **Modelica Standard** | PascalCase | camelCase | camelCase | camelCase |
| **snake_case** | snake_case | snake_case | snake_case | snake_case |
| **Modelica + UPPER_CASE Constants** | PascalCase | camelCase | camelCase | UPPER_CASE |

`All` property returns `IReadOnlyList<(string Name, Func<NamingConventionSettings> Factory)>`.

## Wiring in StyleChecking.RunStyleChecking()

**File:** `ModelicaGraph/StyleChecking.cs`

```csharp
if (settings.FollowNamingConvention)
{
    var config = settings.NamingConvention.ToConfig();
    var visitor = new FollowNamingConvention(config, basePackage);
    visitor.VisitStored_definition(parsedCode);
    violations.AddRange(visitor.RuleViolations);
}
```

## StyleCheckingSettings

**File:** `ModelicaGraph/StyleCheckingSettings.cs`

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `FollowNamingConvention` | `bool` | `false` | Master enable/disable toggle |
| `NamingConvention` | `NamingConventionSettings` | Modelica Standard defaults | Full naming convention configuration |

Both exist in default app settings and per-repository settings (via `Repository.StyleSettings`).

## JSON Serialization

When `FollowNamingConvention` is enabled, the `NamingConvention` object is serialized into `.mlqt/settings.json`:

```json
{
    "FollowNamingConvention": true,
    "NamingConvention": {
        "PresetName": "Modelica Standard",
        "ModelNaming": 2,
        "FunctionNaming": 1,
        "PublicVariableNaming": 1,
        "PublicConstantNaming": 1,
        "AllowUnderscoreSuffixes": true,
        "ExceptionNames": ["NASCAR"]
    }
}
```

Backward compatibility: existing JSON files without `NamingConvention` deserialize with defaults (Modelica Standard).

## UI Components

### NamingStyleSelect.razor

Reusable `MudSelect<NamingStyle>` wrapper with five options: Any (no check), PascalCase, camelCase, snake_case, UPPER_CASE.

Parameters: `Label`, `Value`, `ValueChanged`.

### Settings Panels (SettingsStyleChecking.razor / SettingsRepositories.razor)

Both contain the same naming convention expansion panel, shown when `FollowNamingConvention` is enabled:

- **Preset selector** — Dropdown with all presets + "Custom"
- **Class Names section** — 9 `NamingStyleSelect` dropdowns (model, function, block, connector, record, type, package, class, operator)
- **Public Elements section** — 3 dropdowns (variables, parameters, constants)
- **Protected Elements section** — 3 dropdowns (variables, parameters, constants)
- **Allow underscore suffixes** — Toggle switch
- **Exception Names** — Text field + add button + chip set for removal

When any individual value changes, `PresetName` is set to "Custom". When a preset is selected, all values are replaced with the preset defaults.

### Code-behind Methods

| Method (SettingsStyleChecking) | Method (SettingsRepositories) | Purpose |
|-------------------------------|------------------------------|---------|
| `OnPresetChanged(name)` | `OnRepoPresetChanged(name)` | Apply preset or set "Custom" |
| `OnNamingStyleChanged(action)` | `OnRepoNamingStyleChanged(action)` | Apply change + set "Custom" |
| `AddExceptionName()` | `AddRepoExceptionName()` | Add to exception list |
| `RemoveExceptionName(name)` | `RemoveRepoExceptionName(name)` | Remove from exception list |

### Change Detection (SettingsRepositories.razor)

`StyleSettingsChanged()` includes `!old.NamingConvention.Equals(@new.NamingConvention)` to detect naming convention changes and trigger style re-checking.

## Test Files

| Test File | Coverage |
|-----------|----------|
| `ModelicaParser.Tests/StyleRuleChecks/NamingValidatorTests.cs` | IsCamelCase, IsPascalCase, IsSnakeCase, IsUpperCase, StripSuffix, IsShortAbbreviation, IsValid integration |
| `ModelicaParser.Tests/StyleRuleChecks/FollowNamingConventionTests.cs` | Class names, element names, visibility, suffixes, nested classes, array subscripts, short abbreviations, exception names, quoted identifiers |

## Key Design Decisions

1. **Two-layer config** — `NamingConventionConfig` (record in ModelicaParser) avoids circular dependency; `NamingConventionSettings` (class in ModelicaGraph) handles serialization
2. **Deferred class name checking** — Pending fields ensure `CurrentModelName` is correct when violations are reported
3. **Short abbreviation bypass** — Letter+digits names (T, P3, V12) are always valid — common in Modelica for physical variables
4. **Exception names are case-sensitive** — "NASCAR" is different from "Nascar"
5. **Suffix stripping applies to both class and element names** — When `AllowUnderscoreSuffixes` is enabled, both class names (e.g., `HeatExchanger_simple`) and element names (e.g., `pressure_in`) have the trailing suffix stripped before checking
6. **Array subscript stripping** — `name[3]` → `name` before checking (defensive, as ANTLR grammar typically separates these)
7. **Quoted identifier handling** — Modelica allows quoted identifiers (Q_IDENT grammar rule) like `'r_0'` where single quotes are part of the token text. Both class names and element names have quotes stripped via `StripQuotes()` before convention checking. This ensures `'r_0'` → `r_0` → suffix strip → `r` → valid single char.
8. **`record` for config** — Enables `with` expressions in tests for easy config variation
9. **Nested classes skipped** — `VisitorWithModelNameTracking` skips nested class definitions (depth > 1). Each nested class has its own `ModelNode` and is checked independently, preventing duplicate violations when a parent package's code includes nested class source
