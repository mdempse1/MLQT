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

Because the language selection is committed with the repository while the dictionaries themselves are installed per machine, a repository can ask for a language the machine does not have. MLQT says so rather than quietly checking those words against the remaining languages — the app shows a warning, the MCP server returns a note with the results, and `mlqt check` writes a warning to stderr. If the missing one was the only language, every word is reported as misspelled, and the warning says that too.

### Dictionary Selection Per Repository

Each repository can have its own set of active dictionaries. This is useful when different libraries are documented in different languages — for example, one library might use English documentation while another uses German.

The language selection in repository settings overrides the default language selection.

## Accepted Spellings (Custom Dictionary)

Every library has words that no language dictionary knows and that are not mistakes — company and
product names, domain terminology, abbreviations. Each repository keeps its own list of these, in
`.mlqt/dictionary.txt` inside the working copy, next to the repository's other MLQT settings.

The list is a plain text file, one word per line, sorted, with `#` comment lines allowed. It is meant
to be committed with the code, and editing it outside MLQT is fine — a list that arrives with a
version-control update, or that you edit in a text editor, is picked up on the next check without
restarting. That is the point of storing it in the repository: the desktop app and
a CI run of `mlqt check` read the same file, so they accept the same words and report the same
spelling findings. A list kept on one machine could not do that — a word accepted on a developer's
laptop was still a finding in CI, with nothing in either result to explain the difference.

The trade-off is that a word applies only to the repository it was added to. A term used in three
libraries in three repositories has to be accepted in all three. There is no shared list.

### Managing Accepted Spellings

The word list is in **Settings > Repositories**, under the repository's spell-check options, inside
the **Accepted spellings** expandable section:

- **Add a word** — Type a word in the text field and press Enter or click the **+** button. Case is
  ignored when checking, so a word only needs listing once however it is capitalised, and the
  possessive of a listed word is accepted without listing it separately
- **Remove a word** — Click the delete icon next to any word in the list
- **Filter** — Use the filter text field to search within the word list
- **Import** — Merge words from a text file (one word per line). Existing words are kept; duplicates
  are ignored.
- **Export** — Save this repository's list to a text file, for example to seed another repository

Words can also be added straight from a spelling violation — see below.

### Words From an Earlier Version

Versions before the list moved into the repository kept one machine-wide list at
`%LocalAppData%/MLQT/custom_dictionary.txt`. That file is no longer read when checking. If it exists,
the **Accepted spellings** section shows an extra **Import machine list** button that copies its words
into the repository you are looking at; commit `.mlqt/dictionary.txt` afterwards to share them. Import
it into each repository that needs those words — the old file is left alone, so you can do this at
whatever pace suits.

### Adding Words from Code Review

The fastest way to accept a word is from a spelling violation on the Code Review page — right-click
the underlined word in the code view and choose **Add to Dictionary**. The word goes into the list of
the repository that owns the class you are looking at, not into whichever repository is selected in
settings. See [Correcting Spelling from the Code View](#correcting-spelling-from-the-code-view) below.

If the class belongs to no repository — a library loaded on its own, or one reconstructed from a
vendor's encrypted documentation — there is nowhere to write the word that a check would read back,
so **Add to Dictionary** is disabled and says why.

## Reviewing Spelling Issues

Spelling violations appear in the **Code Review** issues table alongside other style checking issues. Each violation shows the misspelled word, which model it is in, and the line number where it appears.

### Finding a Misspelled Word

When you click a spelling violation in the issues table, MLQT opens the corresponding model and scrolls the code view so the misspelled word is brought into view — so you don't have to hunt for it. The word is **highlighted inline** with a wavy red underline in the rendered Modelica source.

To act on the word — see suggestions, correct it, add it to your dictionary, or ignore it — **right-click the underlined word** in the code view. See [Correcting Spelling from the Code View](#correcting-spelling-from-the-code-view) below.

## Correcting Spelling from the Code View

Misspelled words are **highlighted inline** (wavy red underline) in the rendered Modelica source on the Code Review page. Right-clicking a highlighted word opens a correction menu, letting you fix a typo without leaving the page and have the change written to disk immediately.

1. Right-click a highlighted misspelled word in the rendered code.
2. A correction menu appears just below the word (so it never covers it), automatically nudged to stay within the screen edges. It offers:

   | Option | Action |
   |--------|--------|
   | **Suggestions** | A list of possible correct spellings. Near matches from the repository's accepted spellings come first — mistype a term your team has accepted and the accepted spelling is what you want, not whatever the English dictionary makes of it — followed by the language dictionaries' own suggestions. Click one to apply it in place. |
   | **Replace with** | A text field for typing your own replacement; press **Enter** or click **Apply**. |
   | **Add to Dictionary** | Adds the word to the accepted spellings of the repository this class belongs to. Clicking a possessive records the word itself (`Stodola's` is listed as `Stodola`), since the possessive is then accepted anyway. The word is immediately accepted and **all** violations it covers in that repository are removed. Disabled for classes that belong to no repository. |
   | **Ignore** | Removes this single violation from the issues list without adding the word to the dictionary. The word will be flagged again on the next style check. |
   | **Close** | Closes the menu without taking any action. |

3. When you apply a correction, MLQT replaces the word, **saves the file to disk**, re-parses it, and clears the resolved violation.

The replacement is **whole-word and case-sensitive**, and is only applied inside description strings and documentation prose. Occurrences inside HTML links (`href`s) and `<code>`/`<pre>` blocks are deliberately left untouched, so correcting a word never breaks a link or a code example. If the correction would produce code that fails to parse, the change is aborted and the file is left unchanged.

The rest of the file is left exactly as it was, including its line endings — the corrected word is the only change, so the correction shows up in version control as a one-word diff.

After a correction is applied, the code view reloads but keeps your current scroll position (both vertical and horizontal), so you stay where you were in the file rather than jumping back to the top-left.

> **Note:** This corrects spelling only. Repairing **broken links** in documentation is a separate, planned feature — the spelling correction is careful not to disturb links, but it does not fix ones that are already broken.

### Line Numbers

Spelling violations report the actual line where the misspelled word appears, even within multi-line strings. For documentation annotations that span many lines of HTML, the line number points to the specific line containing the typo, not the line where the annotation starts.

## Tips

- **Enable spell checking after initial library setup.** For large libraries with many existing description strings, you may get a large number of violations initially. Consider reviewing and fixing them in batches, using "Add to Dictionary" liberally for domain-specific terms.

- **Build up the word list early, and commit it.** The first time you enable spell checking on a library, spend some time accepting your project's common terms. This significantly reduces noise in subsequent checks — and because `.mlqt/dictionary.txt` is committed, everyone on the team and every CI run starts from the same list rather than rebuilding it.

- **Use "Add to Dictionary" from Code Review.** This is much faster than navigating to Settings each time — click the violation to jump to the word, right-click the underlined word, and choose "Add to Dictionary". It also puts the word in the right repository for you.

- **Different languages for different repositories.** If your team maintains libraries documented in different languages, set the appropriate dictionaries per repository rather than at the application level.

- **Import dictionaries once, use everywhere.** Imported language dictionaries are stored in your user profile and available across all projects and repositories. You only need to import a dictionary once.
