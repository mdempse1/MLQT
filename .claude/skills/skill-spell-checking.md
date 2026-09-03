# Spell Checking Skill

This skill covers the spell checking system used for Modelica description strings and documentation annotations. It spans the ModelicaParser, ModelicaGraph, MLQT.Services, and MLQT.Shared projects.

## Overview

Spell checking uses **WeCantSpell.Hunspell** (v7.x, MPL 1.1 licensed, fully managed .NET port) to flag misspelled words in:
- **Description strings** (`string_comment` in Modelica grammar) on classes, components, parameters, and constants
- **Documentation annotations** (`Documentation(info=..., revisions=...)` HTML content)

Bundled dictionaries: en_US and en_GB (embedded resources in ModelicaParser). Users can import additional Hunspell dictionaries; accepted words are kept per repository in `.mlqt/dictionary.txt`.

## Architecture

```
ModelicaParser (core logic, no service dependencies)
  SpellChecking/
    SpellChecker.cs          - Hunspell wrapper, thread-safe word checking
    TextExtractor.cs         - HTML stripping, tokenization, word filtering
    Dictionaries/            - Embedded .aff/.dic files + modelica_terms.txt
  StyleRules/
    SpellCheckVisitorBase.cs   - Shared scope handling (names in scope, own + inherited)
    SpellCheckDescriptions.cs  - Visitor for description strings
    SpellCheckDocumentation.cs - Visitor for Documentation annotations

ModelicaGraph (orchestration)
  StyleChecking.cs           - Wires spell check visitors into RunStyleChecking()
  StyleCheckingSettings.cs   - SpellCheckDescription, SpellCheckDocumentation, SpellCheckLanguages

MLQT.Services (lifecycle, persistence)
  StyleCheckingService.cs    - Spell checker lifecycle, lazy init, language invalidation
  StyleCheckingWorker.cs     - Passes spell checker to RunStyleChecking()
  CustomDictionaryService.cs - Per-repo accepted words at <repo>/.mlqt/dictionary.txt
  Checking/DictionaryScope.cs - Which repo's word list applies to a class/library
  DictionaryManagerService.cs - Imported dictionaries at %LocalAppData%/MLQT/Dictionaries/

MLQT.Shared (UI)
  Pages/CodeReview.razor     - Click issue -> scroll/underline word; right-click word -> correction menu (Suggestions, Replace with, Add to Dictionary, Ignore, Close)
    Ignore writes __MLQT(spelling="word") onto the class (MlqtSuppressionWriter) and saves the file
  Components/SettingsRepositories.razor   - Per-repo language selection + Import Language, with a
                                            warning when a chosen language has no dictionary here
                                            (DictionaryAvailability.WarningFor)
  Components/SettingsRepositoryDictionary.razor - Per-repo accepted spellings (add/remove/filter/import/export)
```

## SpellChecker Class

**File:** `ModelicaParser/SpellChecking/SpellChecker.cs`

Factory-created, thread-safe for concurrent reads. Never instantiated directly.

```csharp
// Create with specific languages, custom words, and file-based dictionaries
var checker = SpellChecker.Create(
    languageCodes: ["en_US"],                    // embedded resource codes
    customWords: ["Dymola", "linearization"],    // extra valid words
    additionalDictionaries: [new DictionarySource(affPath, dicPath)]  // file-based
);

// Check a word with optional per-call context words
bool ok = checker.IsCorrect("myWord", contextWords);

// Get suggestions for a misspelled word
IReadOnlyList<string> suggestions = checker.Suggest("misspeling");

// Add a word at runtime (thread-safe)
checker.AddCustomWord("newterm");
```

Key design decisions:
- `WordList.Check()` is thread-safe for concurrent reads
- Accepted words use `HashSet<string>(StringComparer.OrdinalIgnoreCase)` with a `lock` for thread-safe adds
- Embedded dictionaries loaded via `Assembly.GetManifestResourceStream()`
- `modelica_terms.txt` embedded resource provides built-in Modelica-specific terms
- `contextWords` parameter allows callers to pass model-scoped valid words without modifying shared state
- `DictionarySource` record: `(string AffixFilePath, string DictionaryFilePath)` for file-based dictionaries

## TextExtractor Class

**File:** `ModelicaParser/SpellChecking/TextExtractor.cs`

Static utility methods for preparing text before spell checking.

| Method | Purpose |
|--------|---------|
| `StripHtml(html)` | Remove HTML tags, decode entities, collapse whitespace |
| `StripHtmlPreservingNewlines(html)` | Same but preserves `\n` for line number calculation |
| `TokenizeToWords(text)` | Split into `(word, charOffset)` tuples for line mapping |
| `ShouldSkipWord(word)` | Returns `true` for words to skip (see below) |
| `StripQuotes(str)` | Remove surrounding double quotes from STRING tokens |
| `CountNewlinesBefore(text, offset)` | Count `\n` before a character offset |

**Words skipped by `ShouldSkipWord`:**
- Single characters
- Contains non-ASCII characters (decoded HTML entities like `&Delta;` -> `\u0394`)
- Contains digits
- Contains dots, underscores, slashes (qualified names, file paths)
- ALL_CAPS (2+ chars, all uppercase — constants/acronyms)
- camelCase/PascalCase (uppercase letter after lowercase)
- Modelica keywords (`model`, `equation`, `parameter`, etc.)

**HTML stripping details:**
- Content inside `<code>` and `<pre>` tags is removed entirely (code, not prose)
- `PreserveNewlines()` helper counts `\n` in removed content and replaces with same count of newlines, keeping line offsets correct
- Uses `System.Net.WebUtility.HtmlDecode()` for entity decoding

## Style Rule Visitors

Both extend `SpellCheckVisitorBase` (itself a `VisitorWithModelNameTracking`) and follow the standard style rule pattern.

### SpellCheckVisitorBase

**File:** `ModelicaParser/StyleRules/SpellCheckVisitorBase.cs`

Owns the names that count as valid words inside the class being checked, and the `IsSpelledCorrectly(word)` both visitors call.

- **Collected up front.** On entering a class the base scans the class body's element lists for component names and nested class names, so the class's own description string and `Documentation` annotation — both written before the declarations are reached — see every name. Names are pushed/popped per class scope via `OnClassEntered`/`OnClassExited`; `AddNameToScope` still catches declarations the scan does not reach.
- **Inherited names.** The optional `inheritedElementNames` callback, `Func<string modelId, IReadOnlySet<string>>`, supplies the names a class inherits through `extends`. Resolving a base class needs the dependency graph, so the callback comes from `ModelicaGraph.StyleChecking.CreateInheritedElementNamesCallback(graph)` (which uses `ClassElementResolver`, including protected members and the whole chain, cached per class). With no callback only the class's own declarations are known.
- **`OnClassScopeReady(context)`** — hook called once the scope is populated and before the class body is walked; `SpellCheckDescriptions` checks the class description there.
- **Known model names are not merged into the scope.** They are passed straight to `IsCorrect(word, knownModelNames)`; merging built a fresh copy of a set holding every class in the graph for every description string checked.

### SpellCheckDescriptions

**File:** `ModelicaParser/StyleRules/SpellCheckDescriptions.cs`

Checks `string_comment()` on classes (via `OnClassScopeReady`) and `comment().string_comment()` on components. The names in scope come from `SpellCheckVisitorBase`, so references to the class's own and inherited members are not flagged.

**Line number calculation:**
```csharp
var startLine = stringToken.Symbol.Line;
var lineNumber = startLine + TextExtractor.CountNewlinesBefore(text, charOffset);
```

### SpellCheckDocumentation

**File:** `ModelicaParser/StyleRules/SpellCheckDocumentation.cs`

Overrides `VisitElement_modification` to detect `Documentation` -> `info` / `revisions` annotation paths. Uses `StripHtmlPreservingNewlines()` for accurate line counting in multi-line HTML content.

Uses the same scope handling as SpellCheckDescriptions, from `SpellCheckVisitorBase`.

**Violation messages:**
- `"Misspelled word '{word}' in description"` (from SpellCheckDescriptions)
- `"Misspelled word '{word}' in documentation info"` (from SpellCheckDocumentation)
- `"Misspelled word '{word}' in documentation revisions"` (from SpellCheckDocumentation)

## Wiring in StyleChecking.RunStyleChecking()

**File:** `ModelicaGraph/StyleChecking.cs`

```csharp
public static List<LogMessage> RunStyleChecking(
    ModelDefinition _currentModel,
    StyleCheckingSettings settings,
    string fullModelId = "",
    IReadOnlySet<string>? knownModelIds = null,
    SpellChecker? spellChecker = null,
    IReadOnlySet<string>? knownModelNames = null,
    ...
    Func<string, IReadOnlySet<string>>? inheritedElementNames = null)
```

Spell check visitors are instantiated at the end of the method when the corresponding setting is enabled and a `spellChecker` is provided. `inheritedElementNames` is passed to both; `StyleCheckContext.InheritedElementNames` builds it once per check operation (GUI, CLI and MCP all go through `StyleCheckRunner`, so they agree).

## StyleCheckingSettings

**File:** `ModelicaGraph/StyleCheckingSettings.cs`

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `SpellCheckDescription` | `bool` | `false` | Enable description spell checking |
| `SpellCheckDocumentation` | `bool` | `false` | Enable documentation spell checking |
| `SpellCheckLanguages` | `List<string>` | `["en_US", "en_GB"]` | Active language codes |

These settings exist both in the default app settings and per-repository settings (via `Repository.StyleSettings`).

## Spell Checker Lifecycle (Service Layer)

**File:** `MLQT.Services/StyleCheckingService.cs`

`SpellChecker` instances are cached **one per repository root** (`_spellCheckers`), because each
repository has its own accepted words — a shared instance would let one repository's spellings silence
findings in another. An entry is dropped and rebuilt when:
- That repository's languages change (the cached languages are compared on every `EnsureSpellChecker`)
- That repository's word list changes (`OnDictionaryChanged(root)` removes just that entry)
- Imported dictionaries change (`OnDictionariesChanged` clears them all — the language data itself moved)

```csharp
public SpellChecker? GetSpellCheckerIfNeeded(Repository repository)
{
    // ... returns null unless a spell-check rule is on
    return EnsureSpellChecker(repository.LocalPath, settings.SpellCheckLanguages);
}
```

**Dictionary separation:** `CreateSpellChecker()` separates bundled language codes (loaded from embedded resources) from imported ones (loaded from file paths via `IDictionaryManagerService.GetImportedDictionaryPaths()`).

**Accepted words:** `EnsureSpellChecker(repositoryRoot, languages)` takes the words from `ICustomDictionaryService.WordsFor(repositoryRoot)`. The CLI and MCP server go through `SpellCheckerFactory.Build(languages, customWords, dictionaryManager)`, which takes the words as a parameter so every call site has to state whose they are.

## StyleCheckingWorker

**File:** `MLQT.Services/Helpers/StyleCheckingWorker.cs`

Receives an optional `SpellChecker` in its constructor. Builds `knownModelNames` set from `DirectedGraph.ModelNodes` when spell checking is enabled:

```csharp
knownModelNames = _currentGraph.ModelNodes
    .Select(n => n.Id.Contains('.') ? n.Id[(n.Id.LastIndexOf('.') + 1)..] : n.Id)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
```

This ensures any loaded model name (e.g., "Step" from "Modelica.Blocks.Sources.Step") is treated as a valid word.

## Custom Dictionary Service

**File:** `MLQT.Services/CustomDictionaryService.cs`
**Interface:** `MLQT.Services/Interfaces/ICustomDictionaryService.cs`

Accepted words belong to a **repository**, not to a machine or a user: `<repo>/.mlqt/dictionary.txt`,
beside `settings.json`, committed with the code. That is the whole point — the app and `mlqt check`
read the same file, so a word accepted in the app is accepted in CI. A machine-wide list could not
give that, and the resulting disagreement was invisible in both results.

Every method takes the repository root. One word per line, sorted, case-insensitive, `#` comments
allowed. Words are cached per root and re-read when the file changes.

| Method | Purpose |
|--------|---------|
| `WordsFor(root)` | Cached words for a repository; empty for `null`/unknown roots (never someone else's) |
| `LoadAsync(root)` | Read from disk and refresh the cache |
| `AddWordAsync(root, word)` | Add, persist, fire `OnDictionaryChanged(root)` |
| `RemoveWordAsync(root, word)` | Remove, persist, fire `OnDictionaryChanged(root)` |
| `MergeFromAsync(root, file)` | Union a file's words in; returns how many were new |
| `ExportAsync(root, file)` | Write this repository's words out |
| `PathFor(root)` | `<root>/.mlqt/dictionary.txt` |
| `LegacyMachineDictionaryPath` | The pre-move `%LocalAppData%/MLQT/custom_dictionary.txt`, if it still exists — offered for import only, **never read for checking** |

`OnDictionaryChanged` carries the root so listeners can invalidate just that repository.

**`DictionaryScope`** (`MLQT.Services/Checking/DictionaryScope.cs`) is the single answer to "whose list
applies?" — `RootForModel(libraries, repositories, modelId)` and `RootForLibrary(repositories,
library)`. Null means *no list*, not *some other list*: libraries loaded outside a repository, and
encrypted libraries reconstructed from vendor documentation. Use it everywhere rather than re-deriving
the mapping, or the app and the CLI drift apart one level below where they used to.

## Dictionary Manager Service

**File:** `MLQT.Services/DictionaryManagerService.cs`
**Interface:** `MLQT.Services/Interfaces/IDictionaryManagerService.cs`

Manages available Hunspell dictionaries — both bundled (hardcoded list matching embedded resources) and user-imported (stored at `%LocalAppData%/MLQT/Dictionaries/`).

| Method | Purpose |
|--------|---------|
| `GetAvailableDictionaries()` | Returns all dictionaries sorted by display name |
| `ImportDictionaryAsync(affPath, dicPath)` | Copy .aff/.dic pair to user profile, return language code |
| `RemoveImportedDictionaryAsync(langCode)` | Delete imported dictionary files |
| `GetImportedDictionaryPaths(langCode)` | Returns `DictionarySource` for imported dict, null for bundled |

**Display names:** Common language codes (de_DE, fr_FR, etc.) are mapped to readable names ("German", "French"). Unknown codes are displayed as-is.

**Scanning:** On construction, scans the dictionary directory for `.aff` files with matching `.dic` files.

## UI Integration

### Clicking a Spelling Violation (CodeReview.razor)

There is **no click-popover**. Clicking a spelling violation in the issues table (`RowClickEvent`) navigates to the model and arms `_pendingScrollWord` so the misspelled word is scrolled into view and shown underlined once the model renders (see *Scroll-to-mistake from the issues list* below). All correction actions live in the **right-click correction menu** described next.

**Violation detection:** Checks if `LogMessage.Summary` starts with `"Misspelled word '"`. Word is extracted via regex.

### Inline Highlight & Right-Click Correction (CodeReview.razor + CodeViewer.razor)

Misspelled words are highlighted directly in the rendered Modelica source on the Code Review page. Right-clicking a highlighted word opens a correction menu that lets the user apply a fix that is written to disk immediately.

**Highlighting:** `CodeReview` computes the set of misspelled words (`RecomputeMisspelledWords`) from the current spelling violations and passes them to `CodeViewer` via the `MisspelledWords` parameter. `CodeViewer` wraps matching words in markup so they render with a highlight.

**Right-click interop:** `spellCheck.js` attaches a delegated `contextmenu` listener that detects a right-click on a highlighted word and calls the `OnMisspelledWordRightClick` `[JSInvokable]` on `CodeReview`. Rather than the raw cursor position, it captures the clicked word's `getBoundingClientRect()` into a module-level `_anchorRect` and passes the word's **bottom-left** (`r.left, r.bottom`) so the menu opens *below* the word instead of under the cursor (which would cover it).

**Menu positioning (`positionContextMenu`):** The initial position is provisional. `OnMisspelledWordRightClick` sets `_repositionContextMenu = true`; on the next `OnAfterRenderAsync` (once the menu — tagged with the `spell-context-menu` CSS class — has rendered and its real width/height are known) `CodeReview` calls `spellCheck.positionContextMenu(".spell-context-menu", 4)`. The JS measures the menu and `_anchorRect`, aligns the menu's left edge to the word, places it `gap` px below, then **clamps within the viewport**: horizontally into `[margin, vw - margin]`, and vertically flipping *above* the word if it would overflow the bottom (else clamping to the bottom margin). It returns the clamped `[left, top]`, which `CodeReview` writes back into `_contextMenuX/_contextMenuY` so **.NET stays the source of truth** (later keystroke re-renders keep the clamped spot). The flag gates this to run once per open, avoiding loops/jitter. `_anchorRect` is cleared in `dispose`.

**Correction menu options:** (this is the only spelling action surface — the old click-popover was removed)
- One entry per `SpellChecker.Suggest(word)` suggestion — selecting one applies it.
- A free-text input (`_customCorrection`, applied on Enter via `OnCustomCorrectionKeyDown` or the **Apply** button) so the user can type their own spelling when no suggestion fits.
- **Add to Dictionary** (`AddToDictionary`) — adds the word to the list of the repository owning the class being viewed (via `DictionaryScope`) and removes violations for that word within that repository. Disabled, with a tooltip, when the class belongs to no repository.
- **Ignore** (`IgnoreSpellingViolation`) — removes this single violation without touching the dictionary.
- **Close** (`CloseContextMenu`) — dismisses the menu without any action.

**Applying a correction (`ApplyCorrection`):**
1. Resolve the **file-owner** model (topmost model sharing the current node's `ContainingFileId`) — its `Definition.ModelicaCode` is the full physical `.mo` file content.
2. `SpellingCorrector.ReplaceWordInStrings(code, oldWord, newWord)` performs a whole-word, case-sensitive replacement **only inside string literals and documentation prose**, skipping occurrences inside HTML links/`href`s and `<code>`/`<pre>` blocks (so corrections never break links). Aborts if zero replacements were made.
3. Validate the result with `ModelicaParserHelper.ParseWithErrors`; abort (with a Snackbar) on any `FatalParseFailure`.
4. Re-render the whole file through `ModelicaPackageSaver.RenderFileOwnerModel` (reuses the saver's exact renderer config — format-on-save semantics; may reformat beyond the corrected word). The stored `Definition.ModelicaCode` is the **within-less class body**, so `RenderFileOwnerModel` prepends the `within {ParentModelName};` clause (or bare `within;` for a top-level model) before parsing — mirroring the full-save `PreParseModelsParallel` path. This is essential: without the within clause the saved standalone file re-parses with no package context on reload, so the model regenerates a detached, un-prefixed ID and `GetModelById(NavState.ModelID)` returns null (leaving stale code on screen and breaking re-navigation to the class).
5. Pause the file monitor (`StopMonitoring`), `File.WriteAllText` the rendered file, then `StartMonitoring` + `NotifyFileActivity` to refresh VCS indicators.
6. `LibraryDataService.ReloadFileAsync` re-parses the file and rebuilds all its model nodes; `NavState.ModelContentChanged(affected)` invalidates the render cache.
7. Remove the now-fixed spelling violations (`CodeReviewService.RemoveLogMessagesByPredicate`) and re-render.
8. **Scroll preservation:** `ApplyCorrection` captures the `.code-viewer` scroll offsets (vertical and horizontal) via `spellCheck.getScroll` into `_pendingScroll`, recording the current `_highlightedCode` list reference in `_scrollBaselineCode`. Capture happens **just before** the re-render is triggered (past every early-return) so a stale offset can never leak into ordinary navigation. The offsets are restored in `OnAfterRenderAsync` via `spellCheck.setScroll`, gated on `!_isLoadingCode && _highlightedCode is { Count: > 0 } && !ReferenceEquals(_highlightedCode, _scrollBaselineCode)`. The **reference-change** check is the crucial part: the reload assigns a brand-new `_highlightedCode` list, so a changed reference means the *corrected* content is now mounted. Any render of the **old** content that fires between capture and the re-render keeps the same reference and is skipped — without this, the restore fires ~17ms after capture on the stale content and is lost before the real re-render reaches the top. (Earlier attempts to gate on the loading spinner failed: for small/fast files the spinner render coalesces away and `OnAfterRender` never observes `_isLoadingCode == true`.) `spellCheck.setScroll` re-applies the offset across animation frames (retrying ~30 frames until it sticks within 1px, the content genuinely can't scroll that far, or the budget is exhausted) because the freshly-mounted content is not laid out when `OnAfterRender` first runs. (`spellCheck.getScroll`/`setScroll` live in `wwwroot/spellCheck.js`.)
9. **Scroll-to-mistake from the issues list:** clicking a spelling violation in the issues table (`RowClickEvent`) arms `_pendingScrollWord` with `ExtractMisspelledWord(issue)` before `NavState.ChangeModelID`. Once the selected model's content has rendered (`OnAfterRenderAsync`, gated on `!_isLoadingCode && _highlightedCode is { Count: > 0 }`), `spellCheck.scrollWordIntoView(".code-viewer", word)` centres the first `.code-misspell` span whose `data-word` matches. It scrolls to the **highlight span** rather than a line number because the rendered code is re-formatted, so stored file line numbers wouldn't line up. The JS helper retries across frames (the highlight spans are added a beat after the code lines, since `RecomputeMisspelledWords` runs just after the render) and matches `data-word` by value so words with quotes/apostrophes need no escaping.

**Single-file persistence rationale:** Spelling fixes never change class names, so `package.order` is unaffected and a focused single-file write is safe (and avoids the order-corruption risk of a narrowly-scoped `SaveLibraryToDirectoryWithResult`).

> **Deferred:** fixing **broken links** is intentionally out of scope here — `SpellingCorrector` only avoids corrupting links, it does not repair them. A separate "fix broken links" feature is planned.

### Settings - Style Checking (SettingsStyleChecking.razor)

Default settings page includes:
- Toggle switches for `SpellCheckDescription` and `SpellCheckDocumentation`
- Multi-select dropdown for active language dictionaries (bundled + imported)
- "Import Language" button (picks `.aff` file, validates matching `.dic` exists)
- Per-repository accepted-spellings panel (`SettingsRepositoryDictionary.razor`) with add/remove/filter/import/export, plus an "Import machine list" button while the pre-move file still exists

### Settings - Repositories (SettingsRepositories.razor)

Per-repository settings dialog includes the same language multi-select and import button. Language changes are detected in `StyleSettingsChanged()` and trigger style re-checking.

## Adding a New Bundled Dictionary

1. Add `.aff` and `.dic` files to `ModelicaParser/SpellChecking/Dictionaries/`
2. Ensure they are included as `<EmbeddedResource>` in `ModelicaParser.csproj`
3. Add the language code to `SpellChecker.BundledLanguageCodes`
4. Add the language code to `DictionaryManagerService.BundledDictionaries`
5. Update `StyleCheckingSettings.SpellCheckLanguages` default if it should be enabled by default
6. Optionally add a display name mapping in `DictionaryManagerService.FormatDisplayName()`

## Accepting a Word: Three Scopes

| Scope | Where it lives | Written by |
|-------|----------------|------------|
| One class | `__MLQT(spelling="word")` in the class's source | Code Review's **Ignore**, MCP `accept_spelling_in_class` |
| One repository | `<repo>/.mlqt/dictionary.txt` | Code Review's **Add to Dictionary**, the repository dictionary settings page |
| Every check, every repository | `modelica_terms.txt` (embedded) | Editing ModelicaParser |

`__MLQT(spelling="…")` is a comma-separated list on the class, read by `MlqtSuppressionExtractor`
into `SuppressionSet` and applied in `StyleChecking.RunStyleCheckingFindings`'s suppression pass —
so the GUI, the CLI and MCP all honour it, and `mlqt check --no-suppress` audits past it.

- **Word-scoped, not rule-scoped.** Suppressing `MLQT.Spelling.Description` for the class would
  silence every other misspelling in it. `spelling` waives only the listed words.
- **The word comes from the finding's message** (`SpellingMessage.WordFrom`), not its
  `Discriminator`: a documentation finding's discriminator carries the section as well as the word,
  and its shape is part of the fingerprint baselines are keyed on.
- **Case-insensitive, and covers the possessive**, exactly as the repository word list does.
- **Written on a component, read as the class's.** A spelling finding names no element, so a
  component-scoped list could never match one; reading it as the class's keeps such an annotation
  from silently doing nothing.
- `MlqtSuppressionWriter.TryAddSpellingException(ToFile)` writes it, merging into an existing
  `__MLQT`, an existing `spelling` list, or an existing plain annotation. Words carrying a quote or
  a comma are refused — the list is a comma-separated Modelica string.

## Test Files

| Test File | Coverage |
|-----------|----------|
| `ModelicaParser.Tests/SpellChecking/SpellCheckerTests.cs` | SpellChecker creation, IsCorrect, Suggest, custom words, context words |
| `ModelicaParser.Tests/SpellChecking/TextExtractorTests.cs` | HTML stripping, tokenization, ShouldSkipWord, line counting |
| `ModelicaParser.Tests/StyleRuleChecks/SpellCheckDescriptionsTests.cs` | Description checking, component names, model names, multi-line |
| `ModelicaParser.Tests/StyleRuleChecks/SpellCheckDocumentationTests.cs` | Documentation checking, HTML handling, code blocks, line numbers |
| `ModelicaParser.Tests/StyleRuleChecks/SpellCheckInheritedNamesTests.cs` | Names in scope: declared in any order, inherited via the lookup, possessives |
| `ModelicaGraph.Tests/InheritedElementNamesTests.cs` | `CreateInheritedElementNamesCallback` over a graph, and end-to-end with/without the chain |
| `MLQT.Services.Tests/DictionaryManagerServiceTests.cs` | Import, remove, scan, display names, events |
| `MLQT.Services.Tests/StyleCheckingServiceTests.cs` | End-to-end style checking with spell checker stubs |
| `ModelicaGraph.Tests/StyleCheckingTests.cs` | HasAnyStyleRuleEnabled includes spell check settings |
| `ModelicaGraph.Tests/SpellingSuppressionTests.cs` | `__MLQT(spelling="…")` end to end: word scope, class scope, possessives, `--no-suppress` |
| `ModelicaParser.Tests/StyleRuleChecks/MlqtSuppressionWriterTests.cs` | Writing and merging the annotation, including the spelling list |
| `MLQT.McpServer.Tests/SuppressionToolsTests.cs` | `accept_spelling_in_class` |

## Key Design Decisions

1. **No `Suggest()` during background checking** — only called on-demand from the right-click correction menu to avoid performance overhead
2. **Context words are per-call, not shared** — element names are scoped to the class being checked (its own plus everything up its `extends` chain), model names are shared across all checks in a run
3. **Spell checker is cached and invalidated** — recreated only when languages or dictionaries change, not per-model
4. **Accepted words are separate from language dictionaries** — a repository's words are always included regardless of language selection
5. **HTML entity handling** — decoded entities with non-ASCII characters (e.g., `\u0394` from `&Delta;`) are skipped entirely via `ShouldSkipWord`
6. **Line numbers use newline counting** — `StripHtmlPreservingNewlines` + `CountNewlinesBefore` provides accurate line mapping even through HTML removal and `<pre>` blocks
7. **Nested classes skipped** — `VisitorWithModelNameTracking` skips nested class definitions (depth > 1). Each nested class has its own `ModelNode` and is checked independently, preventing duplicate violations
