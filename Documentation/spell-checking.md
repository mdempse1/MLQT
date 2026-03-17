# Spell Checking

MLQT includes built-in spell checking for Modelica description strings and documentation annotations. This helps catch typos in the text that is visible to users of your library — both the short descriptions that appear in component browsers and the full HTML documentation.

## What Gets Checked

Spell checking covers two types of text in Modelica code:

### Description Strings

Description strings are the short text that appears after a class or component declaration:

```modelica
model HeatExchanger "Counter-flow heat exchanger with configurable geometry"
  parameter Real U = 500 "Overall heat transfer coefficeint";  // typo: "coefficeint"
end HeatExchanger;
```

When **Spell check every description string** is enabled, MLQT checks these strings for every class, component, parameter, and constant in your library.

### Documentation Annotations

Documentation annotations contain the HTML content shown in model documentation dialogs:

```modelica
annotation(Documentation(info="<html>
<p>This model implments a counter-flow heat exchanger.</p>  <!-- typo: "implments" -->
</html>"));
```

When **Spell check all documentation** is enabled, MLQT checks both the `info` and `revisions` sections of `Documentation` annotations.

## What Is Not Checked

The spell checker is designed to minimize false positives. The following are automatically skipped:

| Skipped Item | Reason |
|-------------|--------|
| **Modelica keywords** | `model`, `equation`, `parameter`, `extends`, etc. |
| **camelCase and PascalCase identifiers** | `myVariable`, `TimeStep`, `heatTransfer` |
| **ALL_CAPS words** | `MAX_PRESSURE`, `DEFAULT_VALUE` (treated as constants/acronyms) |
| **Words with digits** | `v2`, `step1`, `h20` |
| **Words with dots, underscores, or slashes** | `Modelica.Blocks.Sources`, `file_path`, `path/to/file` |
| **Single characters** | `x`, `T`, `p` |
| **HTML tag names** | `html`, `body`, `div`, `pre` (inside documentation) |
| **Content inside `<code>` and `<pre>` blocks** | Code examples are not prose |
| **HTML entities** | `&Delta;`, `&zeta;`, `&rho;` and their decoded Unicode characters |
| **Component and variable names in scope** | If a model declares `Real rflx`, the word "rflx" is valid within that model's descriptions |
| **Model names from loaded libraries** | Any model name from any loaded library (e.g., "Step", "Integrator", "PID") is treated as a valid word |
| **Modelica-specific terms** | Terms like "Modelica", "Dymola", "OpenModelica", "Jacobian", "linearization" are built in |

## Enabling Spell Checking

Spell checking is controlled by two independent toggle switches, available in both the default settings and per-repository settings:

1. Navigate to **Settings > Style Checking** (for defaults) or **Settings > Manage Repositories** and click a repository (for per-repo settings)
2. Under **Spell checking**, enable one or both:
   - **Spell check every description string**
   - **Spell check all documentation**
3. Save settings

Spell checking runs as part of the background style checking process. After enabling, violations appear in the **Code Review** issues table.

## Language Dictionaries

MLQT ships with English (US) and English (UK) dictionaries. You can select which dictionaries are active and import additional languages.

### Selecting Active Dictionaries

Below the spell checking toggles, a **Language Dictionaries** multi-select dropdown shows all available dictionaries. Select the languages you want to check against — a word is considered correct if it appears in **any** of the selected dictionaries.

By default, both English (US) and English (UK) are selected.

### Importing a New Language

To add support for a new language:

1. Obtain the Hunspell dictionary files for your language. You need two files:
   - A `.aff` file (affix rules) — e.g., `de_DE.aff`
   - A `.dic` file (word list) — e.g., `de_DE.dic`

   Free Hunspell dictionaries for many languages are available from [LibreOffice dictionaries](https://github.com/LibreOffice/dictionaries) and [other open-source sources](https://wiki.documentfoundation.org/Language_support_of_LibreOffice).

2. Click the **Import Language** button (next to the dictionary dropdown)
3. Select the `.aff` file in the file picker — MLQT will automatically look for the matching `.dic` file in the same directory
4. The imported dictionary is copied to your user profile and immediately available for selection

Imported dictionaries are stored at `%LocalAppData%/MLQT/Dictionaries/` and persist across application restarts. They are shown in the dropdown with an "(imported)" label.

### Dictionary Selection Per Repository

Each repository can have its own set of active dictionaries. This is useful when different libraries are documented in different languages — for example, one library might use English documentation while another uses German.

The language selection in repository settings overrides the default language selection.

## Custom Dictionary

The custom dictionary stores words that are not in any language dictionary but should be accepted as correct — company names, product names, domain-specific terminology, abbreviations, etc.

The custom dictionary is shared across all repositories and stored at `%LocalAppData%/MLQT/custom_dictionary.txt`.

### Managing Custom Words

The custom dictionary panel is in **Settings > Style Checking**, inside the **Custom Dictionary** expandable section:

- **Add a word** — Type a word in the text field and press Enter or click the **+** button
- **Remove a word** — Click the delete icon next to any word in the list
- **Filter** — Use the filter text field to search within the word list
- **Import** — Click **Import** to merge words from a text file (one word per line) into the custom dictionary. Existing words are kept; duplicates are ignored.
- **Export** — Click **Export** to save the current custom dictionary to a text file on your desktop

### Adding Words from Code Review

The fastest way to add words to the custom dictionary is directly from a spelling violation in the Code Review issues table. See [Reviewing Spelling Issues](#reviewing-spelling-issues) below.

## Reviewing Spelling Issues

Spelling violations appear in the **Code Review** issues table alongside other style checking issues. Each violation shows the misspelled word, which model it is in, and the line number where it appears.

### Spelling Popover

When you click on a spelling violation in the issues table, a popover appears with four actions:

| Button | Action |
|--------|--------|
| **Add to Dictionary** | Adds the word to your custom dictionary. The word is immediately accepted as correct and **all** violations for that word across all models are removed. |
| **Suggest** | Shows a list of possible correct spellings from the loaded dictionaries. This helps you identify what the correct spelling should be (you still need to fix it in your Modelica code manually). |
| **Ignore** | Removes this single violation from the issues list without adding the word to the dictionary. The word will be flagged again on the next style check. |
| **Close** | Closes the popover without taking any action. |

### Suggestions

When you click **Suggest**, MLQT queries all loaded language dictionaries for words similar to the misspelled word. The suggestions appear in a scrollable list below the action buttons. This is a read-only list — to fix the spelling, edit the Modelica source code directly.

### Line Numbers

Spelling violations report the actual line where the misspelled word appears, even within multi-line strings. For documentation annotations that span many lines of HTML, the line number points to the specific line containing the typo, not the line where the annotation starts.

## Tips

- **Enable spell checking after initial library setup.** For large libraries with many existing description strings, you may get a large number of violations initially. Consider reviewing and fixing them in batches, using "Add to Dictionary" liberally for domain-specific terms.

- **Build up your custom dictionary early.** The first time you enable spell checking on a library, spend some time adding your project's common terms to the custom dictionary. This significantly reduces noise in subsequent checks.

- **Use "Add to Dictionary" from Code Review.** This is much faster than navigating to Settings each time — click the violation, click "Add to Dictionary", and all instances across your library are resolved immediately.

- **Different languages for different repositories.** If your team maintains libraries documented in different languages, set the appropriate dictionaries per repository rather than at the application level.

- **Import dictionaries once, use everywhere.** Imported language dictionaries are stored in your user profile and available across all projects and repositories. You only need to import a dictionary once.
