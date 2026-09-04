# Design Note — Encrypted-library support via vendor help HTML

> **Status: IMPLEMENTED** (2026-08-24). Extends the confidence-aware resolution design in
> [roadmap.md](roadmap.md) §2 — this is the mechanism that moves an encrypted namespace's
> *documented classes* from *"assume valid, never gates"* to *actually resolved*.
>
> **Shipped:** `DymolaHelpParser`/`DymolaHelpReader` (`ModelicaParser/ExternalDocs/`),
> `EncryptedLibraryDetector` (`MLQT.Services/`), `ExternalStubBuilder` +
> `ModelNode.IsExternalStub` (`ModelicaGraph/`), write-path guards, and wiring through
> `LibraryDiscovery`, `ILibraryDataService.AddEncryptedLibraryFromDirectoryAsync`, the CLI's
> `--dependency`, the MCP `load_library` tool, and a **Settings → Reference Libraries** tab in the
> desktop app. User documentation is [encrypted-libraries.md](encrypted-libraries.md).
>
> **Measured against MSL 4.1.0**, which ships both source and generated help so the reconstruction
> can be compared with the truth: 6269 classes recovered, **0** invented, 3.4% of source classes
> undocumented (the protected/hidden ones), 2/5129 descriptions differing, 35/4674 extends lists
> differing, and **0** classes wrongly reported as having no icon. Across all 53 installed encrypted
> libraries, 37,686 classes recovered and every synthesized declaration parses.
>
> **Where that is checked, precisely.** `DocumentationAccuracyTests` performs the comparison above,
> and it needs an installed Dymola — `DymolaInstall` finds one or the suite skips. **No CI runner has
> one**, so those figures are re-measured only on a developer machine that does; this note said
> "checked on every build" and that was never true of a build on a runner (backlog B99). What *does*
> run everywhere is the parser's own suite: 82 fixture-based tests in
> `ModelicaParser.Tests/ExternalDocs/` over `DymolaHelpParser`, `HelpHtml` and `DymolaHelpReader`,
> including the two version-specific facts recorded below. The distinction matters because it is what
> the reviews leaned on when deciding not to read the parser closely: its *shape* is covered on every
> build, its *accuracy against real vendor output* is not.
>
> Two deviations from the plan below, both forced by the evidence and both covered by tests:
> a class heading is now identified by its anchor rather than by being an `<h2>` (vendors put
> their own `<h2>`s inside `Documentation(info=…)`, which silently truncated a package's content
> table); and the "blank icon" is calibrated from the document itself rather than by file size,
> because Dymola draws a *different* placeholder per class restriction.

---

## Purpose

Commercial Modelica libraries ship as a single encrypted `package.moe`. MLQT is a source-based
tool and cannot read them. Any library that extends from, instantiates, or links into one of
these is being checked against a symbol universe with a hole in it:

- **`ValidateModelReferences`** flags every `modelica://Battery.Cells.…` link as broken.
- **`ClassHasIcon`** cannot see an icon inherited from `DymolaModels.Icons.*` and reports
  "missing Icon annotation" on classes that plainly have one.
- **`uses` hygiene**, **unused-class**, and **inherited-member shadowing** resolve nothing
  across the boundary, so they either stay silent or fire falsely.
- **`UsesVersionChecker`** cannot confirm which version of the dependency is actually present.

Almost every encrypted library ships generated HTML documentation alongside the `.moe`, and
that documentation contains the three things the checks actually need: **whether a class
exists**, **what it extends from**, and **whether it has an icon**.

---

## Evidence — survey of `C:\Program Files\Dymola 2026x Refresh 1\Modelica\Library`

53 encrypted libraries (`package.moe`). **52 of them ship a `help/` directory**; one
(`CATIAMultiBody`) does not. Every single help directory is
`<meta name="HTML-Generator" content="Dymola">` — **one format to parse**, not one per vendor
(Dassault, XRG, TLK, Claytex, ClaRa, Cosin all generate through Dymola).

### Layout

```
Battery 2.9.0/
  package.moe          <- encrypted, unreadable
  package.order        <- sometimes present
  libraryinfo.mos      <- sometimes present; carries display name + version
  Resources/           <- NOT encrypted: images, data, scripts
  help/
    Battery.html                                   one HTML file per package
    Battery_BMS_Interfaces.html
    Battery.BMS.Interfaces.CurrentRestrictorI.png  80x80 icon render
    Battery.BMS.Interfaces.CurrentRestrictorS.png  20x20 icon render
```

No encrypted library in this install mixes `.mo` and `.moe` — they are wholly encrypted.

### Scale

| Library | help HTML files | documented classes |
|---|---|---|
| Modelica 4.1.0 *(unencrypted — ships help too)* | 788 | 6269 |
| ElectrifiedPowertrains 1.13.0 | 1128 | — |
| TIL 2026.1 | 440 | — |
| Claytex 2026.1 | 397 | — |
| Battery 2.9.0 | 277 | 1225 |
| FTire 1.3.4 | 7 | — |

**MSL 4.1.0 ships a `help/` directory as well.** That is the single most important fact for
testing: we have a library where both the readable source *and* the generated documentation
exist, so the parser's accuracy can be measured exactly rather than assumed.

### What the HTML gives us, verbatim

```html
<h2><img src="Battery.BMS.Interfaces.CurrentRestrictorI.png"
         alt="Battery.BMS.Interfaces.CurrentRestrictor" align="right" width="80" height="80">
<a name="Battery.BMS.Interfaces.CurrentRestrictor"></a><a href="…#Battery.BMS.Interfaces"
>Battery.BMS.Interfaces</a>.CurrentRestrictor</h2>
<p><span class="ModelicaDescription">Interface model for current restrictor</span></p>
<h3>Information</h3>
<p><span class="ModelicaBaseClass">Extends from
   <a href="Battery_BMS_Interfaces.html#Battery.BMS.Interfaces.BMS">Battery.BMS.Interfaces.BMS</a>
   (Interface model for BMS),
   DymolaModels.Icons.Templates.Box_Bottom (Box with name at bottom).</span></p>
<h3>Connectors</h3>
<table summary="Connectors" class="ModelicaTableConnectors">…</table>
```

- **Existence + fully-qualified name** — `<a name="FQN"></a>` inside the `<h2>`.
- **Description string** — `<span class="ModelicaDescription">`.
- **Base classes** — `<span class="ModelicaBaseClass">`. Links when the target is inside the
  same generated doc set; plain fully-qualified text when it is in another library.
- **Icon** — an `<img>` carried by the class's own `<h2>`, `alt` = the FQN.
- Free extras: children (`ModelicaTablePackageContent`), parameter names + descriptions +
  units, connector names, function inputs/outputs.

### What it does *not* give us

- the class restriction keyword (`model` / `block` / `record` / `connector` / `type` /
  `function` / `package`) — **must be inferred**
- `partial`
- component *types* (names and descriptions only)
- equations, algorithms, annotation graphics
- protected / hidden classes — deliberately absent, and they are not referenceable either

### Two findings that shape the design

1. **Icon PNGs are content-deduplicated behind a name-mangling scheme.**
   `Battery.BMS.Assemblies.Pe0255eb2475cb9f28erBMSI.png` serves three different classes
   (`Interfaces.BMS`, `Assemblies.PerformanceAndObserverBMS`, `Assemblies.ObserverBMS`).
   **Never compute an icon filename from a class name** — always read the `alt`/`src` pairs
   out of the HTML.

2. **The page-owning class — always a package — never carries an `<img>` on its `<h2>`.**
   So the `<h2>` image answers "has icon" for every class *except packages*, and packages are
   exactly what icon-inheritance chains run through (`extends Modelica.Icons.InterfacesPackage`,
   `extends DymolaModels.Icons.Packages.Interfaces`). Handled explicitly below.

---

## Cross-check — OpenModelica ships nothing equivalent *(surveyed 2026-08-24)*

Surveyed `C:\Program Files\OpenModelica1.26.0-64bit` plus the on-demand library store at
`%APPDATA%\.openmodelica\libraries`. **OpenModelica does not follow this pattern, and there is
nothing there to parse.**

- **No per-class HTML documentation, anywhere.** The only HTML in the install is the
  Sphinx-generated *User's Guide* (54 files under `share/doc/omc/OpenModelicaUsersGuide/`) —
  prose about the tool, nothing about library classes. Libraries live as plain `.mo` folder
  trees under `%APPDATA%\.openmodelica\libraries\<Name> <version>`; the 90 HTML files found
  inside the MSL copies are MSL's *own* release-notes documents under
  `Resources/Documentation/`, not generated class docs.
- **No documentation-export API to ask users to run.** `omc`'s scripting builtins
  (`lib/omc/ModelicaBuiltin.mo`) offer `getDocumentationAnnotation` /
  `setDocumentationAnnotation` — read/write a class's own `Documentation` annotation — but
  there is no `generateDoc`/HTML-export equivalent to Dymola's. OMEdit renders documentation
  live from the loaded model rather than emitting a static doc set.
- **Library metadata is JSON, not HTML.** Each installed library carries
  `openmodelica.metadata.json` with `version`, `uses`, `provides`, `convertFromVersion` and the
  source `sha`. Worth reading for the `UsesVersionChecker` story on *unencrypted* OpenModelica
  libraries, but unrelated to the encrypted-library problem.

**OpenModelica's encrypted format is a different — and easier — problem.** Per
`share/doc/omc/OpenModelicaUsersGuide/encryption.html`, OpenModelica uses **SEMLA**
(Standardized Encryption of Modelica Libraries and Artifacts, from Modelon). `buildEncryptedPackage()`
zips the library into a single `PackageName.mol`, and:

- *"The complete folder structure remains as it is"* — the package tree survives the
  encryption, so class names and paths are recoverable from the **archive structure**, not from
  documentation;
- *"No encryption is done on the resource files"*;
- only *"the parts of the library that are protected by the access control annotations defined
  by MLS §18.9"* are encrypted — so a `.mol` is typically **partially** encrypted, and public
  interfaces can remain readable source.

`loadEncryptedPackage`/`parseEncryptedPackage` in `ModelicaBuiltin.mo` confirm `.mol` is a zip
(there is a `skipUnzip` parameter), and `.moc` — the per-file encrypted member extension — plus
the string `semla` both appear in `libOpenModelicaCompiler.dll`. The exact `.moc` container
layout is **unverified**: no `.mol` sample was available to inspect.

**Consequences for this design:**

1. The help-HTML parser is **Dymola-specific by nature, not by choice** — name it accordingly
   (`DymolaHelpParser`, under `ModelicaParser/ExternalDocs/`), and keep the
   `DocumentedClass` record and `DocumentedClassStubBuilder` format-agnostic behind it so a
   second source can be added without disturbing the stub path.
2. That specificity costs nothing today: **all 52 encrypted libraries with documentation ship
   Dymola-generated help**, because that is what the vendors distribute regardless of which
   tool eventually consumes the library.
3. A **`.mol`/SEMLA reader is out of scope for this feature** — assessed in detail below. It is
   not a smaller version of the same job; it is blocked on a trust relationship with each
   library vendor, and the part that *is* available delivers strictly less than the Dymola path.
4. MLS §18.9 access-control annotations (`Protection(access=…)`) are a language-level concept
   MLQT should recognise in its own right — a class marked `Access.hide` is not a candidate for
   "unused public API" findings. Noted as a follow-on, independent of encryption.

### Assessed: cost of adding a `.mol`/SEMLA reader to this feature

**Verdict: don't. Decryption is not available to MLQT at any price in engineering effort, and
the readable subset is a separate, smaller feature that should wait for a real sample.**

#### Why decryption is closed to us

From Modelon's SEMLA specification (`doc/SEMLA.md` in `modelon-community/SEMLA`) and confirmed
against strings in `libOpenModelicaCompiler.dll`:

- The `.mol` **is a plain zip** — OMC extracts it with `ripunzip.exe -q unzip-file -d …`, no
  archive-level encryption. Each library directory carries a `.library/` subfolder holding a
  plaintext `manifest.xml` plus **the vendor's LVE (Library Vendor Executable)**.
- Each protected `.mo` becomes a `.moc`. **The `.moc` container format is deliberately
  undefined** — the spec states the details of the LVE implementation "are at the discretion of
  the Library Vendor". There is no standard format to write a parser *against*; it varies per
  vendor by design.
- Decryption happens **only inside the LVE**, which the tool talks to over a TLS-secured pipe
  protocol (`VERSION`, `LIB`, `FEATURE`, `FILE`, `FILECONTENT`). The tool sends `FILE <path>`;
  the LVE replies `FILECONT <content>`.
- **The LVE holds a list of public keys of the tools it trusts and will only connect to those**;
  the tool's public key is checked during the TLS handshake. This is exactly why OpenModelica
  ships a *separate binary build* containing "an OpenModelica-specific private key", and why
  distributing it requires OSMC Level 2 membership.

So "write a SEMLA parser" is not an engineering estimate. To read one encrypted class, MLQT
would need **each library vendor to add an MLQT key to their LVE's trust list** — a commercial
and certification process, per vendor, with an unknown answer. Estimating days here would be
misleading.

#### What *is* readable without any key

A useful subset, all from the plain zip:

| Available | Gives us |
|---|---|
| Directory structure (preserved by design) | class hierarchy and fully-qualified names |
| `.moc` filenames | class **existence** |
| `package.order` files *(not `.mo`; expected cleartext — **unverified**)* | ordering + names of non-standalone nested classes |
| **Unencrypted `.mo` members** | full source — only §18.9-protected parts are encrypted, so a partially-encrypted library has real source for the rest |
| `Resources/` | unencrypted, per the OM docs |
| `.library/manifest.xml` | library name and version in plaintext |

Note what is **absent**: `extends` clauses and icons for encrypted classes. Those are precisely
the two things beyond existence that this feature exists to recover. A `.mol` reader would
silence false "broken reference" findings but would do **nothing** for `ClassHasIcon` or for
extends-chain resolution — it cannot replace the Dymola documentation path, only sit beside it.

#### Effort, if and when it is wanted

Small on its own terms — unzip via `System.IO.Compression` (in-box, no new dependency), walk
the tree, read `manifest.xml`, and hand any unencrypted `.mo` members straight to the existing
load path, which needs no new code at all. Roughly the size of step 1 of the work breakdown.

But it is **blocked on a sample**: there is no `.mol` on this machine, this OpenModelica build
has no encryption support, and neither the `package.order` cleartext assumption nor the
`manifest.xml` contents could be verified against a real file. Building it against a spec alone
is how it fails on the first real library. **Gate: obtain a sample `.mol` before starting.**

#### The one thing worth folding in now

Make `DocumentedClass` carry **per-field confidence** rather than assuming every source supplies
everything — `ExtendsClasses` and `HasIcon` become "known" / "unknown" rather than empty/false.
A source that can only establish existence then slots into the same `DocumentedClassStubBuilder`,
and the rules that depend on the missing fields degrade to *external, never gates* (roadmap §2
state 2) for that class instead of firing falsely. That costs almost nothing today, keeps the
Dymola path honest about what it did and didn't find, and is what makes a future `.mol` reader a
drop-in rather than a redesign.

---

## The one architectural decision: synthesize stubs, don't add a parallel metadata path

**Option A — synthesize Modelica.** Build one `ModelNode` per documented class whose
`Definition.ModelicaCode` is generated source:

```modelica
within Battery.BMS.Interfaces;
model CurrentRestrictor "Interface model for current restrictor"
  extends Battery.BMS.Interfaces.BMS;
  extends DymolaModels.Icons.Templates.Box_Bottom;
  annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end CurrentRestrictor;
```

**Option B — new metadata fields** on `ModelNode` (`BaseClassNames`, `HasDocumentedIcon`,
`IsExternalStub`) plus a branch in every consumer.

**Take Option A.** Every consumer already works through the parse tree:
`StyleChecking.HasIconInInheritanceChain` calls `Definition.EnsureParsed()` then
`IconExtractor.ExtractIconWithInheritance`; `TypeResolver` / `ClassElementResolver` walk
`extends`; `GraphBuilder.AnalyzeDependenciesAsync` builds edges from the parse;
`CheckModelReferences` only needs the id present in `graph.ModelNodes`. A stub that *parses* is
resolved by all of them with **zero rule changes**. Option B means editing every one of those
and keeping them in step forever — the opposite of the shared-pipeline principle that keeps
GUI, CLI and MCP reporting identical counts today.

The cost of Option A is that stubs look like ordinary nodes to code that **writes**. That is
bought off with exactly one new flag — `ModelNode.IsExternalStub` — whose only job is to make
the write and report paths refuse. See *Write-path guards* below.

---

## Components

| Component | Project | Responsibility |
|---|---|---|
| `EncryptedLibraryDetector` | `MLQT.Services` | Is this directory an encrypted library root? Name, version, help path, resources path. |
| `DymolaHelpParser` → `DocumentedClass[]` | `ModelicaParser/ExternalDocs/` | Parse a `help/` directory into structured class records. |
| `DocumentedClassStubBuilder` | `ModelicaGraph` | `DocumentedClass` → synthesized Modelica → `ModelNode` (with `IsExternalStub`). |
| Load wiring | `LibraryDiscovery`, `ILibraryDataService`, `MLQT.Cli`, `MLQT.McpServer` | Recognise and load encrypted libraries as reference-only dependencies. |

### `DocumentedClass`

```csharp
public sealed record DocumentedClass(
    string FullName,
    string? Description,
    IReadOnlyList<string>? ExtendsClasses,   // null = not known, empty = known to extend nothing
    bool? HasIcon,                           // null = not known
    string? IconImagePath,
    string InferredKind,                     // "package" | "function" | "model" | "class"
    IReadOnlyList<DocumentedMember> Parameters,
    IReadOnlyList<DocumentedMember> Connectors,
    IReadOnlyList<DocumentedMember> Inputs,
    IReadOnlyList<DocumentedMember> Outputs);
```

`ExtendsClasses` and `HasIcon` are **nullable on purpose**: *"the source could not tell us"* and
*"the source told us there is nothing"* are different answers, and conflating them is what turns
a missing input into a false finding. The Dymola parser populates both for every class it reads;
a source that can only establish existence (see the `.mol` assessment above) leaves them null,
and `DocumentedClassStubBuilder` then emits a stub whose extends-chain and icon queries fall
back to *external, never gates* rather than to "extends nothing, has no icon".

### Parser spec

Section the file on `<h2>`; within each section read the fixed markers Dymola emits.

- **FQN** — first `<a name="([^"]+)"></a>` in the `<h2>`.
- **Icon** — an `<img>` in the `<h2>` whose `alt` equals the FQN. Match on `alt`, not on
  position or filename.
- **Description** — first `<span class="ModelicaDescription">…</span>` after the `<h2>`.
- **Base classes** — the `ModelicaBaseClass` span. Per entry: if it is an
  `<a href="…#FQN">`, take the **fragment** (most reliable, and it handles cross-library links
  such as `../../VehicleInterfaces 2.0.2/help/VehicleInterfaces.html#VehicleInterfaces`);
  otherwise take the leading qualified identifier of the plain-text run. Entries are separated
  by `, ` at paren depth zero; the trailing ` (description)` is discarded. **Do not split
  naively on commas** — descriptions contain them. Entries are normally fully qualified, but
  **predefined types appear bare** — `Modelica.Blocks.Interfaces.RealInput` renders as
  `Extends from Real.` Recognise `Real`/`Integer`/`Boolean`/`String`/`enumeration` as builtins
  and drop them from the synthesized stub's `extends` list rather than emitting
  `model X extends Real;`, which is not valid Modelica.
- **Children** — rows of `class="ModelicaTablePackageContent"`; `alt` gives the child FQN and
  `src` its 20 px icon. This also builds the `alt` → `src` map the package-icon fallback needs.
- **Members** — `ModelicaTableParameters` / `…Connectors` / `…Inputs` / `…Outputs`: name +
  description (+ unit, which Dymola appends as `[K]`). Cheap to capture and it unlocks the
  shadowing and missing-units analyses across the boundary later.
- **Kind inference** — `ModelicaTablePackageContent` ⇒ `package`; `…Inputs`/`…Outputs` ⇒
  `function`; `…Connectors` ⇒ `model`; otherwise `class`. Recorded as a *guess* — see Risks.

**No DOM library.** The input is machine-generated with a fixed shape, and the volume is 6k+
classes over 1k+ files per library; a targeted scanner costs nothing and adds no dependency.
AngleSharp is the fallback if the shape turns out to vary across Dymola versions — that would
need `skill-nuget-packages.md` updating.

**Encoding**: files declare `charset=utf-8` and use entities (`&#39;`, `&quot;`, `&amp;`,
`&reg;`). Decode them — descriptions feed spell-checking and the description-string rule.
Note this differs from the `.mo` load path, which reads Latin-1.

### "Has icon" — the package case

For non-package classes the `<h2>` image is definitive. Verified: `Modelica.Units.SI.Length`
(a plain `type`, no icon) has no `<h2>` image, while every iconned class does. Across Battery:
1225 `<h2>` headings, 937 with an image.

For a **package**, the `<h2>` is the page header and never carries one. Two-step fallback:

1. Take the package's 20 px `src` from its **parent's** Package Content row, swap the trailing
   `S.png` for `I.png`, and look for that file.
2. Treat the icon as **absent** if that file is byte-identical to the library's blank icon.
   Dymola renders "no icon" as one fully transparent 80×80 PNG — in MSL 4.1.0, 25 files share
   md5 `bd72a70a4ad885f2a22a955d487417de` at 126 bytes. Detect it per library as the `I.png`
   content hash that is both the most widely shared and under ~200 bytes; cache it. **No image
   decoder needed** — and the size threshold is a tiebreaker, not the test.

### Version

`UsesVersionChecker` needs `ModelNode.Version` on the library root node. Sources, in order:

1. **The directory-name suffix** — `VeSyMA 2026.1` → `2026.1`. This is the Modelica §13.4
   versioned-directory convention: it is what the *tool* resolves against when it decides which
   copy to load, so it is the authoritative statement of which version is actually on the
   machine — which is exactly the question `UsesVersionChecker` asks. It is also the only source
   for the libraries that ship no `libraryinfo.mos` (VeSyMA is one).
2. **`libraryinfo.mos`** → `version="2.9.0"`. Fallback for a directory carrying no version
   suffix (`CATIAMultiBody`, `LinearAnalysis`, `SignalOperators` are unsuffixed in this install).
3. Otherwise null — `UsesVersionChecker` already handles "states no version" correctly.

Because the directory name now leads, the suffix match must be **guarded**: accept it only when
the text after the final space parses as a version (digits and dots, with an optional trailing
build word, matching `UsesVersionChecker.Segments`). An unsuffixed name, or one whose last word
is not version-shaped, falls through to `libraryinfo.mos` rather than being taken literally. Log
at debug which source won, so a surprising mismatch report can be traced to the right file.

The encrypted library's own `uses(…)` is not recoverable and is not needed: the checker compares
what *our* libraries declare against what is loaded.

### Free wins

- `Resources/` is unencrypted, so registering the stub library as a
  `LibraryInfo(name, rootPath)` makes `modelica://Battery/Resources/…` resolve in
  `ExternalResourceService` with no extra work.
- The vendor's real icon PNGs are right there — the library browser can render them for stub
  classes instead of a generic node icon. (`ModelNode.IconSvg` is SVG today; either widen it or
  add `IconImagePath`.)

---

## Write-path guards *(the step that must be exhaustive)*

The highest-severity failure mode is MLQT trying to **rewrite a vendor library it cannot read**.
`IsExternalStub` must be honoured by:

- `ModelicaPackageSaver` — **throw**, don't silently skip, so a missed guard fails a test rather
  than a customer's `Program Files`
- `PackageCodeTrimmer.TrimStandaloneChildren`
- `SaveAllLibrariesWithFormattingAsync` / `SaveChangedFilesWithFormattingAsync`
- `FileMonitoringService` — never monitor a reference library path
- VCS status, commit dialog, `BaselineStatusService`
- `LibraryCheckSession` reported set, `MetricsCalculator` coverage denominators
- Code Review finding list

---

## Where it plugs in

| Surface | Change |
|---|---|
| `LibraryDiscovery.DiscoverLibraryPaths` | recognise a directory containing `package.moe` as a library root (today it only looks for `package.mo`) |
| `ILibraryDataService` | `AddEncryptedLibraryFromDirectoryAsync(path)`; `LoadedLibrary.SourceType = EncryptedDirectory` |
| `MLQT.Cli` `--dependency` | accepts an encrypted library path; the existing *"loaded X for reference resolution (not reported on)"* note already covers it |
| `CheckPipeline` | stubs arrive only via `dependencyPaths`, so they are already outside the reported `models` set. No assertion was added and none is needed: `models` is built from the checked libraries alone, and `LibraryCheckSession` filters stubs centrally in any case |
| `LibraryCheckSession` | no change |
| GUI | new **Reference libraries** setting: a list of directories (e.g. the whole Dymola `Modelica\Library` folder) loaded read-only into the combined graph; scan-on-add finds every library beneath |
| MCP | same path handling in `load_library`; a `list_reference_libraries` tool |

---

## Settings

*(This section planned them for `.mlqt/settings.json`; **they shipped in application settings
instead**, and one of the three was not built. Corrected below rather than left describing something
that does not exist.)*

In `AppSettings.ReferenceLibraries` (the per-project application settings file, **not** a
repository's `.mlqt/settings.json`):

- `Paths: string[]` — directories scanned for reference libraries (encrypted *and* plain source; the
  same mechanism serves "point MLQT at my MSL install").
- `UseEncryptedLibraryDocumentation: bool` (default `true`) — off falls back to the
  "assume valid, never gates" behaviour.

**Why not the repository file**, which is where this note first put them, and which is the
interesting half of the decision: an install location is a property of the *machine*. A colleague's
checkout or a CI runner will not have Dymola at the same path, and baking one into a committed
`.mlqt/settings.json` breaks it for everyone else. CI supplies the equivalent explicitly with the
CLI's `--dependency`. `ReferenceLibrarySettings` says so at the declaration, and
[settings-reference.md](settings-reference.md#reference-libraries) says so to the user.

A library loaded this way is flagged `LoadedLibrary.IsReferenceOnly`, which is what keeps it out of
the checks, the coverage figures and the metrics trend — the *encrypted* ones were covered by
`ModelNode.IsExternalStub`, and a readable one needed a fact of its own (backlog B80).

**`TreatAsExternalNamespaces` was not built.** It belongs to the Wave-2 confidence-aware resolver
(roadmap §2, phase 8) rather than to this feature, and the case it was for — a library shipping no
`help/` at all, `CATIAMultiBody` being the only one in this install — is handled today by that
library simply not resolving, which is the pre-existing "assume valid, never gates" behaviour.

---

## Ordered work breakdown *(each step compiles + tests green)*

1. **Detector + metadata** — `EncryptedLibraryDetector`, versioned directory-name parsing,
   `libraryinfo.mos` reader as the fallback. Tests against the real install, path-gated so they
   skip when Dymola is absent; assert the resolved version for a suffixed library (`Battery 2.9.0`),
   an unsuffixed one (`CATIAMultiBody`), and one with no `libraryinfo.mos` (`VeSyMA 2026.1`).
2. **Help parser** — `DymolaHelpParser` → `DocumentedClass[]`. The substantial piece. Validated
   against MSL (below) before anything depends on it.
3. **Stub synthesis** — `DocumentedClassStubBuilder`, `ModelNode.IsExternalStub`. Round-trip
   test: synthesized code parses, and re-extracting it yields the same name / description /
   extends / icon.
4. **Write-path guards** — the checklist above, each with a test.
5. **Load wiring** — `LibraryDiscovery`, `ILibraryDataService`, CLI `--dependency`, MCP.
6. **GUI reference libraries** — settings UI, read-only tree presentation, vendor icons.
7. **Docs** — new `Documentation/encrypted-libraries.md`; updates to `cli.md`,
   `settings-reference.md`, `ci-quality-gate.md`, `troubleshooting.md`, `CLAUDE.md`,
   and roadmap §2.

---

## Tests — MSL is the ground truth

The decisive test: **MSL 4.1.0 ships both readable source and a Dymola `help/` directory.**
Load MSL twice — once from `.mo` source, once through the help parser — and diff:

- every class id the parser finds exists in the source-loaded graph, and the reverse fraction
  (source classes the docs omit — expected to be the protected/hidden ones) is measured, not
  assumed;
- description strings match after entity decoding;
- `extends` sets match;
- "has icon" matches `IconExtractor.ExtractIconWithInheritance` per class.

That produces a hard accuracy number for the whole approach *before* a line of it ships, and
then stands as a regression test. Report the residual as a known-difference list rather than
asserting 100 %.

Additional coverage:

- unit tests on hand-written HTML fixtures for each marker and each malformed variant
  (missing description, no base classes, base class as plain text vs link, commas inside a base
  class description, entity-encoded description);
- a smoke test walking all 53 installed libraries: no parser exception, plausible class count;
- a library with no `help/` degrades to today's behaviour rather than failing the run;
- write-path guard tests: every saver/formatter path refuses a stub.

---

## Risks

- **Format drift between Dymola versions — measured, and low.** See the version survey below.
  Still mitigate: assert the `HTML-Generator` meta tag, fail *soft* (log + fall back to
  "external") when expected markers are absent, and keep the MSL diff test running against
  whatever Dymola is installed on the build machine.
- **Class kind is inferred, not read.** A `record` and a `connector` both present as "no
  tables". Set `ClassType = "class"` (the neutral restriction) unless inference is confident,
  and never run kind-sensitive rules — naming conventions, kind-based metrics — on stubs.
- **`partial` is invisible.** Stubs cannot be marked partial, so a rule objecting to
  instantiating a partial class cannot run across the boundary. Accept and document it rather
  than guessing from the name.
- **A stub reaching a write path.** Highest-severity failure. Guarded by making
  `ModelicaPackageSaver` throw rather than skip (see above).
- **Doc/source divergence.** The `help/` is generated from the same source as the `.moe`, so
  drift within a release is unlikely — but the docs describe *the shipped version*, which makes
  a version mismatch more consequential here than for source libraries. `UsesVersionChecker`
  covers it once `Version` is populated.
- **Scale.** ElectrifiedPowertrains alone is 1128 HTML files, and a user may point at the whole
  Dymola library folder (~50 libraries, tens of thousands of classes). Parse files in parallel,
  keep `DocumentedClass` allocation-light, and consider caching the parsed result keyed on
  directory path + newest file timestamp.

---

## Format stability — survey across 13 installed Dymola releases *(2026-08-24)*

Compared the generated help across every Dymola on this machine (2021 → 2026x Refresh 1),
using MSL as the common corpus plus `FlexibleBodies 2.4.0` / `Optimization 2.2.8` as
same-version encrypted controls.

**The format is remarkably stable — this is a safe thing to parse.**

| Comparison | Result |
|---|---|
| 2025x → 2025x Refresh 1 (MSL 4.0.0) | help directory **byte-identical**, 0 files differ |
| 2026x → 2026x Refresh 1 (MSL 4.1.0) | help directory **byte-identical**, 0 files differ |
| 2026x → 2026x R1, encrypted libs at same version | only the regenerated `<address>` timestamp, plus genuine library content changes (e.g. `Optimization` gained a record field `GaFTol`). **No format change.** |
| 2025x → 2026x (generated `<head>`) | **two added CSS rules** — `li.unchecked::marker`, `li.checked::marker`. Nothing else. |
| 2025x → 2026x (markup for a sampled class) | **byte-identical** |
| CSS class vocabulary | **identical set of 11** in every version (`ModelicaDescription`, `ModelicaBaseClass`, the five `ModelicaTable*`, `ModelicaVariability::*`, `nobr`) |
| `<h3>` section vocabulary | **identical set of 7** in every version (Information, Parameters, Inputs, Outputs, Connectors, Package Content, Contents) |
| `HTML-Generator` meta, DOCTYPE, `Extends from` phrasing, `<address>` footer | present and unchanged **back to Dymola 2021** |

Every marker the parser spec depends on has been stable for six years and thirteen releases.
Refresh releases are structurally no-ops.

### Two version-specific facts the parser must respect

**1. Dymola 2024x Refresh 1 has a newline-corruption regression.** That one release emits the
literal token `0000000140695720` where newlines belong — **56,894 occurrences**, against 29
legitimate 16-digit numbers in every other release. Verified: all of them sit in whitespace
position (immediately after `>`, immediately before `<`); **zero** land inside a
`ModelicaDescription`, a `ModelicaBaseClass` span, or a `<td>`. So nothing we extract is
corrupted — **but a line-oriented scanner would break on it**, because whole tables collapse
onto one line.

> **Requirement: tokenise the tag stream; never assume line boundaries, and never anchor a
> marker to "the next line".** Ignore stray text between tags. This is not defensive
> programming for a hypothetical — it is required to read a shipped Dymola release.

**2. Icon file naming and dedup DO change between releases.** The same MSL 4.0.0 ships 5,515
icon PNGs under 2024x R1 and 3,924 under 2025x — substantially more aggressive content
deduplication, with no library change at all. This is direct evidence for the design rule
already stated: **never compute an icon filename from a class name; always read the `alt`/`src`
pairs out of the HTML.** The blank-icon content hash (`bd72a70a4ad885f2a22a955d487417de`) is by
contrast stable across 2024x R1 → 2026x R1, so the package-icon fallback holds.

---

## Documented ≠ complete — resolution must be asymmetric

The survey turned up a risk that is **not** about format, and it changes a design decision.

`FlexibleBodies 2.4.0` ships in both 2025x and 2026x under the same version string, but with a
**different `package.moe`** (the vendor rebuilt it without bumping the version). Its
documentation went from **167 classes to 425** — the additions being the entire
`FlexibleBodies.Internal.*` subtree, previously not documented at all.

So: **the documented class set is a subset of the real class set, and the size of that gap is a
vendor decision that varies between releases of the same nominal version.** A class can exist,
be legitimately referenced, and simply not appear in the docs.

**Consequence — resolution against parsed documentation is asymmetric:**

- A **hit** is trustworthy. The class exists; its `extends` and icon are as documented. Findings
  that depend on a hit can gate.
- A **miss** is *not* proof of absence. It must **not** become `MLQT.Reference.Broken` at error
  severity. A reference into a documented-but-encrypted namespace that fails to resolve stays at
  reduced confidence — info at most, never gating.

This narrows the claim made earlier in this note. Parsing documentation does not move an
encrypted namespace wholesale from roadmap state 2 to state 1; it moves the *resolvable* part to
state 1 and leaves the remainder in state 2. That is still a large win — it is what fixes
`ClassHasIcon` and the extends chain — but it does not license treating the encrypted namespace
as complete.

The MSL validation harness measures this gap directly and should report it as a headline number:
*"the docs omit N % of the classes present in source"*. If that figure is material for MSL, it is
material for every commercial library too.

---

## Relationship to the roadmap

This does not replace roadmap §2's three-state confidence model — it **feeds** it. With
documentation parsed, the classes a library actually documents become *resolved* (state 1) and
findings that depend on them can gate. Everything else — a library with no `help/`, a parser
refusal, the feature switched off, **or a class the vendor chose not to document** — stays
*unresolved but external* (state 2) and never gates. Roadmap §2 should be updated to name
documentation parsing as the mechanism that moves *individual classes* between those two states,
not whole namespaces.
