# UI Customization

MLQT's appearance can be customized through the **Settings > UI Settings** tab. You can choose from built-in themes, create custom color schemes, and configure syntax highlighting for Modelica code.

## Accessing UI Settings

1. Click the **Settings** tab (gear icon) in the right panel
2. Select the **UI Settings** sub-tab
3. Make your changes
4. Click **Save Settings** at the bottom to persist your choices

> **[Screenshot: The UI Settings tab showing the UI Theme section with Light/Dark/Custom buttons, and the Syntax Highlighting section below with the theme presets and the code preview.]**

## UI Theme

The UI theme controls the overall look of the application — backgrounds, text colors, button colors, and component styling.

### Built-in Themes

| Theme | Description |
|-------|-------------|
| **Light** | Light backgrounds with a purple-blue primary color. Best for well-lit environments. |
| **Dark** | Dark backgrounds with light text. Reduces eye strain in low-light environments. |
| **Custom** | Define your own color palette using the color pickers below. |

Click any theme button to switch immediately. A checkmark appears on the active theme.

> **[Screenshot: The UI Theme button group showing "Light", "Dark", and "Custom" buttons. The "Dark" button should have a checkmark indicating it's active. The overall UI should reflect the dark theme.]**

### Custom Theme Colors

When you select **Custom**, a set of color pickers appears. Each controls a different aspect of the UI:

| Color | Controls |
|-------|----------|
| **Black** | Darkest UI elements, text on light backgrounds |
| **White** | Lightest UI elements, backgrounds |
| **Primary** | Main accent color — buttons, links, active elements, app bar background |
| **Primary Contrast Text** | Text color on primary-colored backgrounds |
| **Secondary** | Secondary accent color — less prominent interactive elements |
| **Secondary Contrast Text** | Text color on secondary-colored backgrounds |
| **Tertiary** | Third accent color — used sparingly for variety |
| **Tertiary Contrast Text** | Text color on tertiary-colored backgrounds |
| **Info** | Informational elements and borders |
| **Info Contrast Text** | Text color on info-colored backgrounds |

> **[Screenshot: The Custom UI Theme section showing the grid of color pickers. Each picker should show its label and current color value. The UI should reflect the custom theme colors.]**

### Theme Defaults

When switching to **Light** theme, custom colors are reset to the default light palette:
- Primary: `#6a70b1` (purple-blue)
- Secondary: `#666666` (gray)
- Tertiary: `#a18ac1` (light purple)
- Black: `#272c34`, White: `#ffffff`

The **Dark** theme uses MudBlazor's built-in dark palette.

## Syntax Highlighting

The syntax highlighting section controls how Modelica code is displayed in the Code Review tab's code viewer.

### Built-in Syntax Themes

Four preset themes are available:

| Theme | Description |
|-------|-------------|
| **VS Code** | Colors inspired by Visual Studio Code's default light/dark theme. This is the default. |
| **Dymola** | Colors matching the Dymola IDE's Modelica editor. Familiar for Dymola users. |
| **OpenModelica** | Colors matching the OpenModelica Connection Editor. Familiar for OMEdit users. |
| **Custom** | Define your own colors using the color pickers below. |

Each theme automatically adjusts for light or dark mode — when you switch the UI theme between Light and Dark, the syntax highlighting colors update to match.

> **[Screenshot: The Syntax Highlighting button group showing "VS Code", "Dymola", "OpenModelica", and "Custom" buttons. "VS Code" should have a checkmark. Below, the code preview should show a sample Modelica model with the VS Code color scheme.]**

### Custom Syntax Colors

When you select **Custom**, two groups of color pickers appear:

**Editor Colors:**

| Color | Controls |
|-------|----------|
| **Background Color** | The code viewer's background |
| **Text Color** | Default text color for unlexed content |
| **Border Color** | Border around the code viewer |
| **Line Number Color** | Color of line numbers in the left gutter |

**Syntax Element Colors:**

| Color | Controls | Examples |
|-------|----------|----------|
| **Keywords** | Modelica keywords | `model`, `end`, `parameter`, `equation`, `if`, `extends`, `import` |
| **Types** | Built-in type names | `Real`, `Integer`, `Boolean`, `String` |
| **Identifiers** | Variable and parameter names | `x`, `temperature`, `flowRate` |
| **Names** | Class and model names | `MyModel`, `HeatTransfer` |
| **Functions** | Function calls | `sin()`, `cos()`, `loadResource()` |
| **Operators** | Mathematical and assignment operators | `=`, `+`, `-`, `*`, `:=` |
| **Numbers** | Numeric literals | `3.14`, `42`, `1e-6` |
| **Strings** | String literals | `"description text"` |
| **Comments** | Code comments (shown in italic) | `// single line`, `/* block */` |

> **[Screenshot: The Custom Syntax Highlighting section showing the two groups of color pickers and the live code preview below reflecting the custom colors.]**

### Live Code Preview

A preview panel below the syntax highlighting settings shows a sample Modelica model rendered with the current colors. This updates in real-time as you change colors, letting you see the effect immediately:

```modelica
model Example
  // This is a comment
  parameter Real x = 3.14;
  String name = "Modelica";
end Example;
```

> **[Screenshot: Close-up of the code preview panel showing the sample model with distinct colors for each syntax element — keywords in one color, types in another, identifiers in another, etc.]**

## Saving and Resetting

### Save Settings

Click the **Save Settings** button at the bottom of the Settings panel to persist your UI and syntax highlighting changes. Without saving, changes are applied to the current session but will be lost when you restart MLQT.

### Reset to Defaults

Click **Reset to Defaults** to restore all settings on the current tab to their original values.

## Tips

- **Dark mode + Dymola theme** works well for extended editing sessions — the dark background reduces eye strain while the familiar Dymola colors keep the code readable.
- **Custom themes** are useful for accessibility — you can increase contrast or choose colors that work best for your vision.
- Theme changes take effect **immediately** — you don't need to restart the application.
- UI themes and syntax highlighting are **personal settings** stored on your machine. They are not shared with your team via the repository.
