# Naming Conventions

MLQT can check that class names, variable names, parameter names, and constant names in your Modelica code follow a consistent naming convention. This helps maintain readability and consistency across your library.

## What Gets Checked

The naming convention checker validates two categories of identifiers:

### Class Names

Every Modelica class type can have its own naming convention:

| Class Type | Default Convention | Examples |
|------------|-------------------|----------|
| model | PascalCase | `HeatExchanger`, `SimplePipe` |
| function | camelCase | `calculatePressure`, `interpolate` |
| block | PascalCase | `PIDController`, `LimiterBlock` |
| connector | PascalCase | `FluidPort`, `HeatPort` |
| record | PascalCase | `ThermodynamicState`, `FluidData` |
| type | PascalCase | `Temperature`, `MassFlowRate` |
| package | PascalCase | `HeatTransfer`, `FluidMechanics` |
| class | PascalCase | `PartialModel`, `BaseClass` |
| operator | PascalCase | `Complex`, `Quaternion` |

### Element Names

Element names are checked based on their visibility (public or protected) and category (variable, parameter, or constant):

| Visibility | Category | Default Convention | Examples |
|------------|----------|--------------------|----------|
| Public | Variable | camelCase | `pressure`, `massFlowRate` |
| Public | Parameter | camelCase | `diameter`, `nominalPressure` |
| Public | Constant | camelCase | `pi`, `boltzmannConstant` |
| Protected | Variable | camelCase | `internalState`, `tempValue` |
| Protected | Parameter | camelCase | `defaultTolerance` |
| Protected | Constant | camelCase | `maxIterations` |

## Available Naming Styles

Each naming rule can be set to one of five styles:

| Style | Pattern | Examples |
|-------|---------|----------|
| **Any (no check)** | Anything accepted | — |
| **PascalCase** | Starts uppercase, no underscores | `MyModel`, `HeatExchanger` |
| **camelCase** | Starts lowercase, no underscores | `myVariable`, `massFlowRate` |
| **snake_case** | All lowercase with underscores | `my_variable`, `heat_transfer` |
| **UPPER_CASE** | All uppercase with underscores | `MAX_PRESSURE`, `BOLTZMANN` |

## Names That Are Always Accepted

Certain names bypass convention checking entirely:

- **Short abbreviations** — A single letter optionally followed by digits: `T`, `p`, `x`, `P3`, `V12`, `T2`. These are well-established physical variable names in Modelica (temperature, pressure, position, etc.) and are too common to enforce conventions on.

- **Exception names** — Names you explicitly add to the exception list. This is useful for product names (e.g., `NASCAR`), established abbreviations (e.g., `OMC`), or any name that intentionally breaks the convention. Exception names are case-sensitive.

## Quoted Identifiers

Modelica supports quoted identifiers using single quotes, e.g., `'r_0'` or `'my.special.name'`. The naming convention checker automatically strips the surrounding quotes before checking the name. For example, `'r_0'` is checked as `r_0`, which with suffix stripping becomes `r` — a single character that is always valid.

## Underscore Suffixes

Modelica code commonly uses underscore-separated suffixes to indicate direction or purpose:

```modelica
connector FluidPort
  Real pressure_in;    // "_in" is a direction suffix
  Real temperature_a;  // "_a" is a port indicator
  flow Real m_flow;    // "_flow" is a quantity suffix
end FluidPort;
```

When **Allow underscore suffixes** is enabled (the default), the naming checker strips the last underscore-separated segment before checking the convention. So `pressure_in` is checked as `pressure` (which is valid camelCase), and `m_flow` is checked as `m` (a single letter, always valid). Only a single trailing underscore suffix is stripped — the base name before the underscore is what gets checked against the naming style.

## Presets

MLQT includes three predefined naming convention presets:

### Modelica Standard (Default)

The standard Modelica convention used by the Modelica Standard Library and most commercial libraries:
- PascalCase for all class types except functions (camelCase)
- camelCase for all elements (public and protected)
- Underscore suffixes allowed

### snake_case

All identifiers use snake_case. Underscore suffixes are disabled (since underscores are part of the naming convention itself).

### Modelica + UPPER_CASE Constants

Same as Modelica Standard, but constants (both public and protected) use UPPER_CASE:
- `constant Real MAX_PRESSURE = 1e6;`
- `constant Integer DEFAULT_SIZE = 10;`

## Enabling Naming Convention Checking

1. Navigate to **Settings > Style Checking** (for defaults) or **Settings > Manage Repositories** and click a repository (for per-repo settings)
2. Enable **Check that the naming convention is followed**
3. An expansion panel appears below the toggle showing the current convention settings
4. Select a preset or customize individual rules
5. Save settings

Naming convention violations appear in the **Code Review** issues table alongside other style checking issues. Each violation identifies the offending name, what convention it should follow, and whether it is a class name or element name.

![Screenshot: The naming convention expansion panel showing the preset dropdown set to "Modelica Standard", the Class Names section with nine NamingStyleSelect dropdowns, the Public Elements and Protected Elements sections, the Allow underscore suffixes toggle, and the Exception Names area with a text field and chip set.](Images/naming-conventions-1.png)

### Customizing Rules

When you change any individual naming rule after selecting a preset, the preset name automatically changes to "Custom". This indicates that the current configuration does not match any predefined preset.

To return to a preset, select it from the preset dropdown — this replaces all individual settings with the preset defaults.

### Adding Exception Names

To add a name that should always be accepted regardless of the naming convention:

1. In the **Exception Names** section of the naming convention panel, type the name in the text field
2. Click the **+** button (or press Enter)
3. The name appears as a chip below the text field
4. To remove an exception, click the close button on its chip

Exception names are case-sensitive — adding "NASCAR" does not accept "Nascar" or "nascar".

### Additional Allowed Patterns

For cases where you need to allow a *pattern* of names rather than individual exceptions, each naming slot supports additional allowed regex patterns. A name is valid if it matches the base naming style OR any additional pattern configured for that slot.

This is useful when a convention has systematic exceptions. For example, if model class names should be PascalCase but documentation release notes classes use a versioned format like `Version_2026_1`:

1. Next to any naming style dropdown, click the **filter icon** to expand the pattern editor
2. Enter a regex pattern (e.g., `^[A-Z][a-zA-Z]+(_\d+)+$` to match PascalCase followed by underscore-digit segments)
3. Click the **+** button to add the pattern
4. The pattern appears as a chip and the filter icon shows a badge with the pattern count
5. To remove a pattern, click the close button on its chip

Patterns are scoped per slot — a pattern added to "model" does not apply to "function" or other class types. This allows different exception patterns for different naming contexts.

Patterns are matched against the **full original name**, not the suffix-stripped version. The regex must match the entire intended name format.

Adding patterns sets the preset to "Custom". Invalid regex patterns are rejected with an error message when you try to add them.

**Note on bracket-wrapped patterns:** If you have manually edited `.mlqt/settings.json` and accidentally wrapped a pattern in square brackets (e.g., `[^[A-Z]...$]` instead of `^[A-Z]...$`), MLQT will automatically detect and correct this. The outer brackets are stripped when the inner content contains regex anchors (`^` or `$`), restoring the intended pattern semantics.

## Violation Examples

With Modelica Standard conventions enabled, the following code would produce violations:

```modelica
model simpleModel          // Violation: Class name 'simpleModel' should be PascalCase (model)
  Real MyVariable;         // Violation: Variable name 'MyVariable' should be camelCase (public variable)
  parameter Real BadParam; // Violation: Parameter name 'BadParam' should be camelCase (public parameter)
  Real pressure_in;        // OK: suffix stripped, "pressure" is camelCase
  Real T;                  // OK: single letter, always valid
  Real P3;                 // OK: short abbreviation (letter + digits), always valid
end simpleModel;
```

## Per-Repository Settings

Each repository can have its own naming convention, independent of the default settings and other repositories. This is useful when:

- Different libraries follow different conventions
- A legacy library uses snake_case while new libraries use PascalCase
- Some teams prefer UPPER_CASE constants while others do not

Repository naming convention settings are stored in `.mlqt/settings.json` inside the repository, so they are shared with your team through version control.

## Tips

- **Start with the Modelica Standard preset.** It matches the conventions used by the Modelica Standard Library and most commercial Modelica libraries. Only customize if your team has specific needs.

- **Use exception names sparingly.** If you find yourself adding many exceptions, consider whether the convention itself needs adjusting. Exception names are intended for genuinely unique cases like product names.

- **Enable underscore suffixes.** Modelica code commonly uses suffixes like `_in`, `_out`, `_a`, `_b` for ports and direction indicators. Disabling suffix stripping will produce many false positives in typical Modelica code.

- **Consider UPPER_CASE for constants.** Many teams find it helpful to visually distinguish constants from variables. The "Modelica + UPPER_CASE Constants" preset provides this with minimal changes from the standard convention.

- **Review violations incrementally.** When first enabling naming convention checking on an existing library, you may see many violations. Address them in batches rather than trying to fix everything at once.
