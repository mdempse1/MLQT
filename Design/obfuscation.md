# Obfuscation readiness (B502)

**Not shipped. Retire this note when it is**, moving what is durable into `CODING_GUIDELINES.md`
and the tests that hold it.

A consumer building on MLQT ships the MCP stack inside its own product and protects it with an
obfuscator at its strongest settings: renaming of every type, member, parameter and enum member,
**public ones included** (it merges MLQT into its own assemblies), with string encryption,
control-flow obfuscation, method-body encryption, anti-tamper and resource encryption.

**In scope**: the seven assemblies of that stack — `ModelicaParser`, `ModelicaGraph`,
`RevisionControl`, `DymolaInterface`, `OpenModelicaInterface`, `MLQT.Services`, `MLQT.McpServer`.
`MLQT.Shared`, `MLQT.Photino` and `MLQT.Cli` are not shipped by that consumer and are not made safe
here; where they read a file this work changes, they read it through the same code, so every MLQT
surface still writes the same bytes.

## What breaks, and why

Renaming changes anything whose **format is taken from a .NET name at run time**. Three surveys of
the source (2026-10-14) found it in five places in scope. Nothing else in scope looks a name up by
string: no `Type.GetType`, `Activator`, `Enum.Parse`, `GetProperty("x")`, stack-trace parsing or
attribute scanning of MLQT's own types.

| # | Where | What a renamed build would do |
|---|-------|-------------------------------|
| 1 | **MCP tool parameter names** become the input schema and the argument binding (`classId`, `preview`, …, about 180 across 66 tools) | Every client call fails to bind |
| 2 | **MCP results and complex arguments**: 70 DTO records and 4 anonymous types serialized by reflection; 8 enums written as `ToString()` | Every result's field names change; `mlqt_batch_edit` and `mlqt_set_style_settings` cannot read their arguments |
| 3 | **Committed repository files**: `.mlqt/settings.json` (`StyleCheckingSettings`, `NamingConventionSettings`, `RuleSeverity` names), `.mlqt/baseline.json`, `.mlqt/metrics-history.json` | Writes files MLQT and the CLI cannot read, and cannot read theirs — silently, since unknown properties are ignored |
| 4 | **Settings store** (`mcp-settings.json`, `settings.json`): `RepositorySettingsCollection` | The project list reads as empty; a store written by another build is lost |
| 5 | **Dymola JSON-RPC**: three anonymous `{method, params, id}` envelopes; `CloseHandle` P/Invoke named by its method | No call reaches Dymola |

Constructor-bound records (`BaselineFile`, `MetricsSnapshot`, …) fail a second way: reflection
binds constructor parameters to properties **by name**, so a build that renames the two differently
cannot even read the file it wrote.

**Seen and accepted**, because they only make diagnostics harder to read and the consumer's mapping
file decodes them: exception type names in parse-failure messages and logs, the `--timings` keys
built from a rule visitor's type name, stack traces in the log file.

## The approach: safe by construction, attribute only where nothing else works

An obfuscator's configuration belongs to the consumer and can change under us, so a contract is made
independent of names in the code wherever that is possible, and verified here by a test:

- **JSON contracts use System.Text.Json source generation** (`JsonSerializerContext`). The generator
  writes each property's name, and each constructor call, into the assembly at compile time — before
  any obfuscator runs — so nothing is looked up by name at run time. Each context is used **alone**
  where the contract is MLQT's own, so a type left out of it fails loudly instead of quietly falling
  back to reflection. A test holds each context to the names reflection produces today, so the
  change moves no byte of any existing file or result.
- **Enum names written as text** come from one explicit mapping (`[JsonStringEnumMemberName]` for
  `RuleSeverity`, which the converter writes; a switch for the MCP results), held complete by a test.
- **No anonymous type is serialized** in scope; a source-scanning test holds it.
- **P/Invoke names its entry point.**
- **MCP tool parameter names are the one thing code cannot make name-independent** — the SDK reads
  `ParameterInfo.Name` — so the 17 tool classes carry `[Obfuscation]`, which an obfuscator that
  honours `System.Reflection.ObfuscationAttribute` reads as "do not rename". A test holds
  every tool class to it. **Whether it keeps parameter names is to be confirmed on a protected
  build.**

## Done

All five rows above are fixed in code and held by tests on the plain build: `JsonContractTests`,
`ServicesJsonContractTests`, `RuleSeverityNameTests`, `ObfuscationReadinessTests` (MCP results,
anonymous objects, enum text, the tool classes' `[Obfuscation]`, the options reaching every tool) and
`JsonRpcRequestTests`. Each guard was checked by breaking what it holds. The rules are in
`CODING_GUIDELINES.md` §Names That Must Survive Obfuscation.

## Verification

The proof is a build protected at the strongest settings: the seven assemblies obfuscated together,
then `build/obfuscation/mcp_session.py` - one scripted session over stdio, deterministic run to
run - against the protected server and the plain one, whose two transcripts must be identical.
Its `--time-check` mode times a load and a check of a large library (control flow and string
encryption on the ANTLR parser are the performance risk). It needs a licence for the obfuscator, so
it is a release step (`Design/release-checklist.md` §7), not a CI gate.

Open until then: whether `[Obfuscation]` keeps parameter names; whether resource encryption keeps
`GetManifestResourceStream` working for the spelling dictionaries (`SpellChecker.OpenBundled`);
whether method-body encryption and anti-tamper coexist with SkiaSharp, LibGit2Sharp and the parser's hot paths,
and at what cost.
