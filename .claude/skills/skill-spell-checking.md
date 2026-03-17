# Spell Checking Skill

This skill covers the spell checking system used for Modelica description strings and documentation annotations. It spans the ModelicaParser, ModelicaGraph, MLQT.Services, and MLQT.Shared projects.

## Overview

Spell checking uses **WeCantSpell.Hunspell** (v7.x, MPL 1.1 licensed, fully managed .NET port) to flag misspelled words in:
- **Description strings** (`string_comment` in Modelica grammar) on classes, components, parameters, and constants
- **Documentation annotations** (`Documentation(info=..., revisions=...)` HTML content)

Bundled dictionaries: en_US and en_GB (embedded resources in ModelicaParser). Users can import additional Hunspell dictionaries and maintain a custom word list.

## Architecture

```
ModelicaParser (core logic, no service dependencies)
  SpellChecking/
    SpellChecker.cs          - Hunspell wrapper, thread-safe word checking
    TextExtractor.cs         - HTML stripping, tokenization, word filtering
    Dictionaries/            - Embedded .aff/.dic files + modelica_terms.txt
  StyleRules/
    SpellCheckDescriptions.cs  - Visitor for description strings
    SpellCheckDocumentation.cs - Visitor for Documentation annotations

ModelicaGraph (orchestration)
  StyleChecking.cs           - Wires spell check visitors into RunStyleChecking()
  StyleCheckingSettings.cs   - SpellCheckDescription, SpellCheckDocumentation, SpellCheckLanguages

MLQT.Services (lifecycle, persistence)
  StyleCheckingService.cs    - Spell checker lifecycle, lazy init, language invalidation
  StyleCheckingWorker.cs     - Passes spell checker to RunStyleChecking()
  CustomDictionaryService.cs - User word list at %LocalAppData%/MLQT/custom_dictionary.txt
  DictionaryManagerService.cs - Imported dictionaries at %LocalAppData%/MLQT/Dictionaries/

MLQT.Shared (UI)
  Pages/CodeReview.razor     - Spelling popover: Add to Dictionary, Suggest, Ignore, Close
  Components/SettingsStyleChecking.razor  - Default language selection + custom dictionary management
  Components/SettingsRepositories.razor   - Per-repo language selection
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
- Custom words use `HashSet<string>(StringComparer.OrdinalIgnoreCase)` with a `lock` for thread-safe adds
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

Both extend `VisitorWithModelNameTracking` and follow the standard style rule pattern.

### SpellCheckDescriptions

**File:** `ModelicaParser/StyleRules/SpellCheckDescriptions.cs`

Checks `string_comment()` on classes and `comment().string_comment()` on components. Collects component/variable names per class scope as context words.

**Scoped context words:** As the visitor traverses, it collects component names from `component_declaration.declaration.IDENT()` into a per-class `HashSet<string>`. These are passed to `IsCorrect(word, contextWords)` so references to local variables in descriptions are not flagged.

**Line number calculation:**
```csharp
var startLine = stringToken.Symbol.Line;
var lineNumber = startLine + TextExtractor.CountNewlinesBefore(text, charOffset);
```

### SpellCheckDocumentation

**File:** `ModelicaParser/StyleRules/SpellCheckDocumentation.cs`

Overrides `VisitElement_modification` to detect `Documentation` -> `info` / `revisions` annotation paths. Uses `StripHtmlPreservingNewlines()` for accurate line counting in multi-line HTML content.

Same scoped component name collection as SpellCheckDescriptions. Same context words pattern.

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
    IReadOnlySet<string>? knownModelNames = null)
```

Spell check visitors are instantiated at the end of the method when the corresponding setting is enabled and a `spellChecker` is provided.

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

The `SpellChecker` instance is lazily created and cached. It is invalidated and recreated when:
- Language selection changes (`_lastLanguages` tracks current languages)
- Custom dictionary changes (`OnDictionaryChanged` event from `ICustomDictionaryService`)
- Imported dictionaries change (`OnDictionariesChanged` event from `IDictionaryManagerService`)
- `ReloadSpellChecker()` is called explicitly

```csharp
private SpellChecker? GetSpellCheckerIfNeeded(StyleCheckingSettings settings)
{
    if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
        return EnsureSpellChecker(settings.SpellCheckLanguages);
    return null;
}
```

**Dictionary separation:** `CreateSpellChecker()` separates bundled language codes (loaded from embedded resources) from imported ones (loaded from file paths via `IDictionaryManagerService.GetImportedDictionaryPaths()`).

**Custom dictionary loading:** On first use, `EnsureSpellChecker` calls `ICustomDictionaryService.LoadAsync()` to ensure the custom dictionary is loaded from disk before creating the spell checker.

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

Persists user-specific custom words at `%LocalAppData%/MLQT/custom_dictionary.txt`. One word per line, sorted, case-insensitive. Shared across all repositories.

| Method | Purpose |
|--------|---------|
| `LoadAsync()` | Load from disk (called lazily on first spell check) |
| `AddWordAsync(word)` | Add word, persist, fire `OnDictionaryChanged` |
| `RemoveWordAsync(word)` | Remove word, persist, fire `OnDictionaryChanged` |
| `ImportAsync(filePath)` | Replace dictionary with file contents |
| `ExportAsync(filePath)` | Write current dictionary to file |
| `MergeAsync(filePath)` | Union file words into current dictionary |

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

### Code Review Popover (CodeReview.razor)

When a spelling violation is clicked, a popover appears with four actions:

| Button | Action |
|--------|--------|
| **Add to Dictionary** | Adds word to custom dictionary, removes ALL violations for that word across all models |
| **Suggest** | Calls `SpellChecker.Suggest(word)`, displays scrollable list of suggestions |
| **Ignore** | Removes this single violation from the review |
| **Close** | Closes popover without any action |

**Violation detection:** Checks if `LogMessage.Summary` starts with `"Misspelled word '"`. Word is extracted via regex.

### Settings - Style Checking (SettingsStyleChecking.razor)

Default settings page includes:
- Toggle switches for `SpellCheckDescription` and `SpellCheckDocumentation`
- Multi-select dropdown for active language dictionaries (bundled + imported)
- "Import Language" button (picks `.aff` file, validates matching `.dic` exists)
- Collapsible custom dictionary panel with add/remove/filter/import/export

### Settings - Repositories (SettingsRepositories.razor)

Per-repository settings dialog includes the same language multi-select and import button. Language changes are detected in `StyleSettingsChanged()` and trigger style re-checking.

## Adding a New Bundled Dictionary

1. Add `.aff` and `.dic` files to `ModelicaParser/SpellChecking/Dictionaries/`
2. Ensure they are included as `<EmbeddedResource>` in `ModelicaParser.csproj`
3. Add the language code to `SpellChecker.BundledLanguageCodes`
4. Add the language code to `DictionaryManagerService.BundledDictionaries`
5. Update `StyleCheckingSettings.SpellCheckLanguages` default if it should be enabled by default
6. Optionally add a display name mapping in `DictionaryManagerService.FormatDisplayName()`

## Test Files

| Test File | Coverage |
|-----------|----------|
| `ModelicaParser.Tests/SpellChecking/SpellCheckerTests.cs` | SpellChecker creation, IsCorrect, Suggest, custom words, context words |
| `ModelicaParser.Tests/SpellChecking/TextExtractorTests.cs` | HTML stripping, tokenization, ShouldSkipWord, line counting |
| `ModelicaParser.Tests/StyleRuleChecks/SpellCheckDescriptionsTests.cs` | Description checking, component names, model names, multi-line |
| `ModelicaParser.Tests/StyleRuleChecks/SpellCheckDocumentationTests.cs` | Documentation checking, HTML handling, code blocks, line numbers |
| `MLQT.Services.Tests/DictionaryManagerServiceTests.cs` | Import, remove, scan, display names, events |
| `MLQT.Services.Tests/StyleCheckingServiceTests.cs` | End-to-end style checking with spell checker stubs |
| `ModelicaGraph.Tests/StyleCheckingTests.cs` | HasAnyStyleRuleEnabled includes spell check settings |

## Key Design Decisions

1. **No `Suggest()` during background checking** — only called on-demand from the UI popover to avoid performance overhead
2. **Context words are per-call, not shared** — component names are scoped to the model being checked, model names are shared across all checks in a run
3. **Spell checker is cached and invalidated** — recreated only when languages or dictionaries change, not per-model
4. **Custom dictionary is separate from language dictionaries** — custom words are always included regardless of language selection
5. **HTML entity handling** — decoded entities with non-ASCII characters (e.g., `\u0394` from `&Delta;`) are skipped entirely via `ShouldSkipWord`
6. **Line numbers use newline counting** — `StripHtmlPreservingNewlines` + `CountNewlinesBefore` provides accurate line mapping even through HTML removal and `<pre>` blocks
7. **Nested classes skipped** — `VisitorWithModelNameTracking` skips nested class definitions (depth > 1). Each nested class has its own `ModelNode` and is checked independently, preventing duplicate violations
