# MLQT.UI.Theme

Loaded when working in `MLQT.UI.Theme/`. The repository-wide rules are in the root `CLAUDE.md`.

MLQT's look as a Razor class library, so that other Blazor tools can look like MLQT by referencing
it. `MLQT.Shared` builds on it too, so the application and every other tool take their appearance
from one place. **`MLQT.McpTester` is the consumer to copy.** It references the theme and nothing
else of MLQT, builds its `MudTheme` with `MlqtTheme.BuildTheme`, and writes its host page from
`ThemeAssetManifest` with its own `app.css` in the middle. `HostAssetManifestTests` holds that page,
as it holds MLQT's. It is **built on outside this repository** and its public surface is tracked in
`PublicAPI.*.txt`, like the six assemblies the root `CLAUDE.md` lists.

## What belongs here

Only what has nothing of MLQT in it: a rule or component another tool would want unchanged.

| Here | Stays in MLQT.Shared |
|------|----------------------|
| `MlqtTheme`: the default palettes, typography (body text 0.75rem, `Body2` monospace) and layout sizes | `CustomPalette`: the palette from the user's custom colours, because it reads `UISettings` |
| `mlqt-base.css`: the `--mlqt-*` tokens, page font, scrollbars (the WebKitGTK overlay fix, B140), the tab host and page-fill flex chain, the split-pane proportions and divider | `app.css`: the findings pane, resource tree, find-in-code match, the Dependencies impact row, the library tree pane |
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
- **Class names and `--mlqt-*` tokens are public API**, listed in `PublicCss.txt` the way types
  are in `PublicAPI.*.txt`. `ThemeCssContractTests` (MLQT.Shared.Tests) fails on a class or token
  the stylesheets define and the list does not, and on one listed that is gone. It also fails on a
  `var(--mlqt-*)` anywhere in the repository's pages that the theme does not define, which CSS
  would otherwise treat as the property's initial value. Rename one as you would a public type.
- **A value belongs in a token** when the stylesheets repeat it or a tool might want it: type,
  the 4/8/16px spacing scale, control and icon sizes, colours. All of them live in the `:root`
  block at the top of `mlqt-base.css`. An optical correction to one control (a 5px or 11px padding)
  stays a literal. A token MudBlazor already has is that variable, not a copy:
  `--mlqt-font-size` is `--mud-typography-body1-size`, so the controls follow MlqtTheme's
  typography. `--mlqt-control-border` is deliberately **not** `--mud-palette-lines-default`. They
  are the same colour in the light palette, but MudBlazor's turns white in dark mode, and aliasing
  it would change the dark theme.
- **`ThemeTokensJourney`** checks in a real page that the tokenised rules come out at the theme's
  sizes. It also measures the scrollbar gutter, which is the B140 fix and depends on `var()`
  resolving inside `::-webkit-scrollbar`. That measurement only means anything under
  `MLQT_JOURNEY_BROWSER=webkit`, since headless Chromium draws no scrollbars, so it skips under
  Chromium. Run it under WebKit after touching that rule.
- **MudBlazor is referenced here and nowhere else** in the repository, so a consumer gets the
  version the stylesheets were written against. An upgrade is a change to this project, and the
  `nuget-packages` skill.
- **RS0041 is off for the project** (`NoWarn` in the `.csproj`): the Razor compiler generates each
  component's `BuildRenderTree` outside any nullable context. A scoped `.editorconfig`, which is how
  ModelicaParser silences it for ANTLR output, does not reach source-generator output.
- **Its tests are in `MLQT.Shared.Tests`** (`Theming/MlqtThemeTests`, `Components/ProgressDialogTests`,
  the font and order tests in `HostAssetManifestTests`), and the coverage gate measures it from there.
