# MLQT.UI.Theme

Loaded when working in `MLQT.UI.Theme/`. The repository-wide rules are in the root `CLAUDE.md`.

MLQT's look as a Razor class library, so that other Blazor tools can look like MLQT by referencing
it. `MLQT.Shared` builds on it too, so the application and every other tool take their appearance
from one place. It is **built on outside this repository** and its public surface is tracked in
`PublicAPI.*.txt`, like the six assemblies the root `CLAUDE.md` lists.

## What belongs here

Only what has nothing of MLQT in it: a rule or component another tool would want unchanged.

| Here | Stays in MLQT.Shared |
|------|----------------------|
| `MlqtTheme`: the default palettes, typography (body text 0.75rem, `Body2` monospace) and layout sizes | `CustomPalette`: the palette from the user's custom colours, because it reads `UISettings` |
| `mlqt-base.css`: page font, scrollbars (the WebKitGTK overlay fix, B140), the tab host and page-fill flex chain, the split-pane proportions and divider | `app.css`: the findings pane, resource tree, find-in-code match, the Dependencies impact row, the library tree pane |
| `mlqt-density.css`: MudBlazor made small and dense - tabs, buttons, trees, icons, tables, toolbars | MainLayout's own rules: the Modelica tree icon, the runnable startup step |
| `fonts/`: Roboto, bundled with its licence, so no tool needs a network to render | Syntax and diff colours, `ThemePresets`: Modelica's, and the user's to change |
| `mudBlazorWorkarounds.js`: fixes to MudBlazor's own stylesheet | Cytoscape, the diff and spell-check interop |
| `ProgressDialog`: an inline dialog the application opens and closes (B414) | Product logos and the favicon (`Branding/`): each tool keeps its own |

**No brand name and no logo.** Each tool keeps its own, and the repository names no real company.

## The rules a change here has to keep

- **The stylesheets go either side of MudBlazor's, and the order is the contract.** `mlqt-base.css`
  is loaded *before* MudBlazor.min.css, where MLQT's `app.css` always was; `mlqt-density.css`
  *after*, because each of its rules overrides one of MudBlazor's at the same specificity and wins
  only by coming later. `ThemeAssetManifest` states the order as leading and trailing lists around
  the tool's own assets, `HostAssetManifest` composes MLQT's page from them, and
  `HostAssetManifestTests` holds both. A host page that puts the density file before MudBlazor's
  renders MudBlazor's default sizes with no error anywhere.
- **The density file was an inline `<style>` in MainLayout**, which came after MudBlazor's sheet *and*
  after the `<style>` MudThemeProvider writes into the body. As a stylesheet in the head it now
  comes before that one. Nothing in it competes with what MudThemeProvider writes (custom properties,
  and the scrollbar rule `mlqt-base.css` overrides with `!important`), and the documentation
  screenshots matched before and after the move - but a new rule here that targets something
  MudThemeProvider also styles will lose to it.
- **Class names, custom properties and asset paths are public API** that no analyzer sees.
  `mlqt-split`, `mlqt-page-fill`, `compact-table` and the rest are used by name in other repositories;
  rename one as you would rename a public type.
- **MudBlazor is referenced here and nowhere else** in MLQT.Shared's chain, so a consumer gets the
  version the stylesheets were written against. An upgrade is a change to this project, and the
  `nuget-packages` skill.
- **RS0041 is off for the project** (`NoWarn` in the `.csproj`): the Razor compiler generates each
  component's `BuildRenderTree` outside any nullable context. A scoped `.editorconfig`, which is how
  ModelicaParser silences it for ANTLR output, does not reach source-generator output.
- **Its tests are in `MLQT.Shared.Tests`** (`Theming/MlqtThemeTests`, `Components/ProgressDialogTests`,
  the font and order tests in `HostAssetManifestTests`), and the coverage gate measures it from there.
