# MLQT.UI.Theme

MLQT's look as a Razor class library, so that another Blazor tool can look like MLQT by
referencing it. MLQT itself is built on it, so the theme and the application cannot drift apart.

It gives you:

- **`MlqtTheme`**: MLQT's MudBlazor palettes, its typography (body text at 0.75rem, `Body2`
  monospace) and its layout sizes (a 2rem app bar).
- **`mlqt-base.css`**: the `--mlqt-*` tokens, classic scrollbars on Linux (WebKitGTK otherwise draws
  overlay bars over the content), and the layout classes for split panes and tab hosts that fill a
  window.
- **`mlqt-density.css`**: MudBlazor made small and dense, covering tabs, buttons, trees, icons,
  tables and toolbars.
- **Roboto**, bundled with its licence, so the tool needs no network to render in the right typeface.
- **`mudBlazorWorkarounds.js`**: takes out a MudBlazor selector that makes WebKit slow.
- **`ProgressDialog`**: an inline dialog the application opens and closes, which closes however soon
  it is told to after it was told to open. A bound `MudDialog` does not always.

No logo and no product name: each tool keeps its own.

## Using it

**1. Reference the project** from source, and **do not also reference MudBlazor**. The theme pins
the version its stylesheets are written against, and a second reference could resolve to a
different one.

```xml
<ProjectReference Include="path/to/MLQT/MLQT.UI.Theme/MLQT.UI.Theme.csproj" />
```

**2. Register MudBlazor's services** as usual: `builder.Services.AddMudServices();`

**3. Give MudBlazor the theme** in your layout:

```razor
@using MLQT.UI.Theme

<MudThemeProvider Theme="@_theme" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

@code {
    private static readonly MudTheme _theme = MlqtTheme.BuildTheme(MlqtTheme.GetDefaultPaletteLight());
}
```

A plain `<MudAppBar>` comes out in MLQT's primary colour, the same as MLQT's own, because the
palettes set its background to that colour.

**4. Load the assets in the order `ThemeAssetManifest` gives**, with your own around the middle:

```text
ThemeAssetManifest.LeadingStylesheets    _content/MLQT.UI.Theme/mlqt-base.css
your own stylesheets
ThemeAssetManifest.TrailingStylesheets   _content/MLQT.UI.Theme/fonts/roboto.css
                                         _content/MudBlazor/MudBlazor.min.css
                                         _content/MLQT.UI.Theme/mlqt-density.css
YourApp.styles.css                       (your scoped-CSS bundle)

the Blazor bootstrap script
ThemeAssetManifest.LeadingScripts        _content/MudBlazor/MudBlazor.min.js
your own scripts
ThemeAssetManifest.TrailingScripts       _content/MLQT.UI.Theme/mudBlazorWorkarounds.js
```

**The order matters, and getting it wrong gives no error.** `mlqt-density.css` overrides MudBlazor's
rules at the same specificity and wins only by coming after `MudBlazor.min.css`. Put it before that
sheet and the page renders at MudBlazor's default sizes. A page generated at runtime can loop over
the lists. A static `index.html` should be checked against them by a test, as this repository
checks both of its host pages in `MLQT.Shared.Tests/HostAssetManifestTests.cs`.

`MLQT.McpTester` in this repository is a complete working example: one project reference, the
layout above, and a static page in this order.

## What you can rely on

The theme is built on outside this repository, so its surface is tracked:

- **Types and members** are listed in `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`, and the
  build fails on any change to them that is not listed.
- **CSS classes and `--mlqt-*` tokens** are listed in `PublicCss.txt`, and a test fails on any
  change to them that is not listed. Use the classes by name (`mlqt-split`, `mlqt-page-fill`,
  `compact-table` and the rest), and read or override the tokens:

  ```css
  :root { --mlqt-space-2: 10px; }               /* a looser toolbar padding */
  .my-row.selected { background: var(--mlqt-highlight-selected); }
  ```

  The text-size tokens are MudBlazor's typography variables, so they follow `MlqtTheme`.

What the theme does **not** do: size your page. MLQT's window is one screen that never scrolls and
sizes its `MudLayout` to the viewport itself; a tool whose page scrolls should not, so the theme
leaves `.mud-layout` at MudBlazor's default.
