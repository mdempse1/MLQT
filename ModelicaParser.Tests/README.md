# ModelicaParser.Tests

The tests for the ModelicaParser library: the parser and renderer (formatter), the style rules, the
extractors, icon and diagram rendering, spell checking, encrypted-library documentation and the
change classifier. About 120 test files and well over 2,000 `[Fact]`/`[Theory]` methods. The
assembly is held to **>95% coverage per class** (root `CLAUDE.md`), measured by
`build/check-coverage.ps1`.

## Test Structure

| Folder / files | Covers |
|----------------|--------|
| `ModelicaRendererTests/` | `ModelicaRenderer`: formatting of every construct, line wrapping, comments, section and declaration order, markup mode. (The `Debug*` files are kept regression cases.) |
| `StyleRuleChecks/` | The style-rule visitors and `CheckFailure`/finding plumbing |
| `Visitors/` | Syntax colouring visitors and annotation elision |
| `Icons/` | Coordinate systems, icon/diagram SVG rendering, icon text |
| `SpellChecking/` | `SpellChecker`, corrections, text extraction, concurrency |
| `ExternalDocs/` | Reading an encrypted library's generated help HTML |
| `Comparison/` | `ClassChangeClassifier` and cosmetic-change signatures |
| `Helpers/` | The `ModelicaParser.Helpers` language tables (names, sections, built-ins) |
| top-level `*Tests.cs` | Extractors, the token classifier, file encoding and line-ending preservation, error listener, grammar coverage, and the policy tests (`*PolicyTests.cs`) that hold a rule over the whole assembly |
| `Resources/` | Larger `.mo` fixtures, copied to the output |

## Test Helper Methods

`TestHelpers.AssertClass(testModel, ...)` parses a whole class, renders it with `ModelicaRenderer`
and asserts the result equals `expectedOutput` (or the input itself when none is given - the input
is written already formatted). Its optional arguments switch on the formatting options: line length,
one-of-each-section, imports first, components before classes, declaration order, and markup mode
(`renderForCodeEditor`). `TestHelpers.FormatCode` returns the formatted text without asserting, for
idempotency tests that feed the output back in.

## Running Tests

The project runs on xUnit v3 / Microsoft.Testing.Platform (see the root `CLAUDE.md`).

```bash
dotnet test ModelicaParser.Tests
dotnet test ModelicaParser.Tests --filter "FullyQualifiedName~ModelicaRendererTests"
```

### Opt-in real-library corpus

`ModelicaTokenClassifierTests` and `Visitors/AnnotationElisionCoverageTests` include a fidelity check
over real libraries that runs only when `MLQT_FIDELITY_CORPUS` names one or more library roots, so an
ordinary run says nothing about it. Run it after changing `ModelicaTokenClassifier`; the
`test-tooling` skill has how.
