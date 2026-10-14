# Obfuscation readiness (B502)

**Verified on protected builds (see *Verification*); retire this note once it ships**, moving what
is durable into `CODING_GUIDELINES.md` and the tests that hold it.

A consumer building on MLQT ships the MCP stack inside its own product and protects it with an
obfuscator at its strongest settings: renaming of every type, member and parameter, **public ones
included** (it merges MLQT into its own executable), with string encryption, control-flow
obfuscation, method-body encryption, code virtualization, call hiding, internalization of public
types and resource encryption. **Internalization deletes an enum's members from metadata**, not just
renames them; a consumer measured `Enum.GetValues` going from 5 to 0 on a five-member enum.

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
| 6 | **Enum metadata**: `RuleSeverity` written by `JsonStringEnumConverter`, which reflects over the members; anything sized from or checking `Enum.GetValues`/`IsDefined` | With the members deleted, severities are written as numbers, and an array sized from `GetValues` has length zero |

Reflection does not even produce renamed names under these settings: a record serialized by
reflection comes out as `{}`, every property gone, and reads back as defaults - silent both ways.

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
- **Enum names written as text** come from explicit switches - `RuleSeverityText` and
  `RuleSeverityJsonConverter` for the files, `WireNames` for the MCP results - held complete by tests,
  and **no enum's metadata is read** at all (`EnumMetadataPolicyTests`). The converter reads exactly
  what the reflection converter read before B502, lower case included, which the first version of this
  work had broken by pinning names with `[JsonStringEnumMemberName]`.
- **No anonymous type is serialized** in scope; a source-scanning test holds it.
- **P/Invoke names its entry point.**
- **MCP tool parameter names are the one thing code cannot make name-independent** — the SDK reads
  `ParameterInfo.Name` — so the 17 tool classes carry `[Obfuscation]`, which an obfuscator that
  honours `System.Reflection.ObfuscationAttribute` reads as "do not rename". A test holds every tool
  class to it. **Whether it is honoured is the consumer's setting**: one consumer found an obfuscator
  ignoring the attribute unless its declarative-protection option was on, with no warning. So
  `ToolParameterCheck` checks known parameters when the server is registered and refuses to start,
  saying what to change, rather than bind no client's arguments.

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

Measured on 2026-10-14 with a trial of the obfuscator, all seven assemblies protected together:
renaming of public names, string encryption, control flow at level 9, method-body encryption,
anti-tamper, resource encryption. The mapping file confirms the renaming reached what the contracts
protect - `StyleCheckingSettings`, `MetricsSnapshot`, `BaselineEntry`, `RepositorySettingsCollection` and every
MCP result record were renamed, members included - while the tool classes kept their names.

- **The protected server answers the session exactly as the plain one does**, and the session calls
  **every one of the 66 tools** successfully - the edits as previews, then real writes and a reload:
  the instructions, all input schemas, every result, quoted arguments, the `.mlqt/settings.json` it
  wrote, the embedded dictionaries, a SkiaSharp image of a wired diagram, a routed connection and a
  LibGit2Sharp status. MSL loads and checks to the same 74 findings over 6,487 classes.
- **The comparison catches a broken build**: the commit before this work, protected the same way, came
  back with renamed result fields (`{"dbb8oue6SC": "Smoke.Described", ...}`), most results empty, a
  different settings file and four broken argument schemas.
- **Parameter names survived even before `[Obfuscation]`**: in that control, only the four tools taking
  a record argument had a different schema. So with these settings the obfuscator does not rename
  parameters, and the attribute is not what keeps them; it stays because it costs nothing and holds
  the tool classes and methods under a configuration that does.
- **Cost**: two runs loading MSL took 5.2 s and 2.9 s plain against 7.0 s and 6.0 s protected
  (136% and 210%); its check, 0.39 s and 0.35 s against 0.66 s and 0.48 s. The first call into
  protected code pays about a second once (`mlqt_server_info` 25 ms against 1,007 ms). The load is
  parsing, so if a consumer finds it too slow, the generated ANTLR parser - partial classes, so an
  attribute can be added beside them - is where to exclude control flow first. The other hot spot is
  the diagram image: 0.24 s plain against 3.8 s protected for the three-part wired fixture.
- **Not something obfuscation did**: `mlqt_check_library` lists its findings in the order its workers
  finish, which differs between two plain runs. The session sorts them before comparing.
- **A semantic scan for enums becoming text** (every interpolation, `ToString()`, concatenation and
  formatting argument of an enum type, across the seven assemblies) found two more that are written
  where a renamed name would matter - the VCS a settings store records, and a baseline's drift
  warning - now spelled and held by `SpelledEnumNameTests`. The rest are in-memory keys, which agree
  within a run, debug text, and the runtime's own enums, which are not renamed. A name-based search
  had missed both, which is why the scan was semantic.

**A second configuration, merged** (2026-10-14), matching a consumer's: all seven assemblies merged
into the server's own, the library DLLs deleted and `deps.json` cut to match, with internalization,
unprintable names, renaming of all parameters, method-body encryption, code virtualization, control
flow 9, pre-JIT, call hiding, resource encryption and strong-name protection, anti-tamper off. Seven
of the merged assembly's 44 enums were left with no members in metadata, so deletion was real.

- **The session is identical** - all 66 tools - with the exclusion attribute honoured, with the
  declarative-protection option off, with the attribute removed from the tool classes altogether,
  and with the native-executable host.
- **The file on disk is not what runs.** Its parameter table was empty and its resource names
  scrambled, yet at run time the server reported every parameter name and the dictionaries loaded:
  the protection restores both as the assembly loads. Inspecting a protected file says little about
  its behaviour; only running it does, which is why the comparison is a session, not a scan.
- **The attribute could not be shown necessary here**: with this version and these settings the tool
  parameters survived without it. A consumer reports the opposite with its settings, so the
  attribute and `ToolParameterCheck` both stay - the check turns a silent failure into a refusal to
  start, and is tested against renamed parameters.
- **Run the comparison with anti-tamper off.** A consumer measured anti-tamper producing builds that
  fail their own integrity check at start-up, intermittently, from identical input (3 of 12 trials);
  a session failing that way looks like a regression and is not one.
- **The `--timings` keys** built from a rule visitor's type name reach only `CheckTimings` - the CLI's
  `--timings` and the log - never a fingerprint or a file, unlike the analyzer names that once fed
  fingerprints.
