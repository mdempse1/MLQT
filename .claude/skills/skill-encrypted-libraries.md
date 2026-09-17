# Encrypted Libraries Skill

Load this skill when working on `ModelicaParser/ExternalDocs/` (`DymolaHelpParser`,
`DymolaHelpReader`, `HelpHtml`, `DymolaHelpDocument`), `EncryptedLibraryDetector`,
`ExternalStubBuilder`, `ModelNode.IsExternalStub`, reference libraries, or anything that asks
"can MLQT see inside this library?".

User-facing documentation is [encrypted-libraries.md](../../Documentation/encrypted-libraries.md).

## The problem and the answer

Commercial Modelica libraries ship as a single encrypted `package.moe`. MLQT is a source-based
tool, so without help those libraries are opaque: every reference into them is unresolved, every
inherited icon is invisible, and every `extends` chain stops at the boundary.

**Dymola generates a `help/` directory of HTML beside the encrypted package**, and that HTML names
each documented class, its description, its base classes and whether it has an icon. MLQT reads it
and synthesizes a minimal Modelica declaration per class, so that every parse-tree-based consumer
resolves those classes **with no rule changes at all**.

## The one architectural decision: synthesize stubs, never a parallel metadata path

`ExternalStubBuilder` turns a `DocumentedClass` into a `ModelNode` whose `ModelicaCode` is
generated source:

```modelica
within Battery.BMS.Interfaces;
model CurrentRestrictor "Interface model for current restrictor"
  extends Battery.BMS.Interfaces.BMS;
  annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end CurrentRestrictor;
```

The rejected alternative was new metadata fields on `ModelNode` plus a branch in every consumer.
Every consumer already works through the parse tree — `StyleChecking.HasIconInInheritanceChain`,
`TypeResolver`, `ClassElementResolver`, `GraphBuilder.AnalyzeDependenciesAsync`,
`CheckModelReferences` — so a stub that *parses* is resolved by all of them for free, while the
metadata design means editing every one of them and keeping them in step forever. That is the
opposite of the shared-pipeline principle that keeps GUI, CLI and MCP reporting identical counts.

The cost is that a stub looks like an ordinary node to code that **writes**, and that is bought off
with exactly one flag.

## `IsExternalStub` — the write-path guards must be exhaustive

The highest-severity failure mode is MLQT rewriting a vendor library it cannot read, in the user's
`Program Files`. `ModelNode.IsExternalStub` exists only to make the write and report paths refuse:

- `ModelicaPackageSaver` — **throws, never silently skips**, so a missed guard fails a test rather
  than a customer's installation
- `PackageCodeTrimmer.TrimStandaloneChildren`
- the full and incremental formatting paths
- `FileMonitoringService` — a reference library path is never monitored
- VCS status, the commit dialog, `BaselineStatusService`
- `LibraryCheckSession`'s reported set, `MetricsCalculator`'s coverage denominators
- the Code Review finding list

Backlog B85 was this guard reached from a direction nobody had checked: the MCP edit tools would
overwrite a vendor's `package.moe` with Modelica text whenever the library happened to sit somewhere
writable. `IsWritable` answers false for a stub too, so `get_class_info` stops advertising one as
editable and inviting the attempt.

**A readable reference library is a different fact.** `ModelNode.IsExternalStub` covers encrypted
ones; a plain-source library loaded for reference needed `LoadedLibrary.IsReferenceOnly` of its own
(backlog B80), and that is what keeps it out of the checks, the coverage figures and the metrics
trend.

## What the HTML gives, and what it does not

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
```

**Recovered:** existence and fully-qualified name, description string, base classes, whether the
class has an icon and which image file it is, children, parameter/connector/input/output names with
descriptions and units.

**Not recovered, and must not be guessed:** the class restriction keyword (`model` / `record` /
`connector` / …), `partial`, component *types*, equations, algorithms, annotation graphics, and
protected classes — which are deliberately absent and are not referenceable either.

`DocumentedClass.ExtendsClasses` and `HasIcon` are **nullable on purpose**: *"the source could not
tell us"* and *"the source told us there is nothing"* are different answers, and conflating them
turns a missing input into a false finding.

## Parser rules that are requirements, not style

**Tokenise the tag stream. Never assume line boundaries, and never anchor a marker to "the next
line."** Dymola 2024x Refresh 1 emits the literal token `0000000140695720` where newlines belong —
56,894 occurrences in one release. Every one sits in whitespace position (after `>`, before `<`) and
none lands inside a `ModelicaDescription`, a `ModelicaBaseClass` span or a `<td>`, so nothing
extracted is corrupted — **but a line-oriented scanner collapses whole tables onto one line and
breaks.** This is required to read a shipped Dymola release, not defensive programming.

**Never compute an icon filename from a class name.** Icon PNGs are content-deduplicated behind a
name-mangling scheme: `Battery.BMS.Assemblies.Pe0255eb2475cb9f28erBMSI.png` serves three unrelated
classes. The dedup is also *not* stable between releases — the same MSL 4.0.0 ships 5,515 icon PNGs
under Dymola 2024x R1 and 3,924 under 2025x, with no library change. Always read the `alt`/`src`
pairs out of the HTML.

**A class heading is identified by its anchor, not by being an `<h2>`.** Vendors put their own
`<h2>`s inside `Documentation(info=…)`, which silently truncated a package's content table when the
parser sectioned on the tag.

**Base classes: take the link fragment where there is one**, otherwise the leading qualified
identifier of the plain-text run. Split entries on `, ` at paren depth zero — **never split naively
on commas**, because descriptions contain them — and discard the trailing ` (description)`.
Predefined types appear **bare**: `Modelica.Blocks.Interfaces.RealInput` renders as
`Extends from Real.` Recognise `Real`/`Integer`/`Boolean`/`String`/`enumeration` as builtins and drop
them, or the stub emits `model X extends Real;`, which is not valid Modelica.

**Decode HTML entities.** The files declare `charset=utf-8` and use `&#39;`, `&quot;`, `&amp;`,
`&reg;`. Descriptions feed spell-checking and the description-string rule. Note this differs from the
`.mo` load path, which detects encoding per file and falls back to Latin-1 — see
`ModelicaFileEncoding`.

**No DOM library.** The input is machine-generated with a fixed shape and the volume is 6k+ classes
over 1k+ files per library; a targeted scanner costs nothing and adds no dependency. AngleSharp is
the fallback if the shape ever varies, which would mean updating `skill-nuget-packages.md`.

### "Has icon" — the package case

For a non-package class the `<h2>` image is definitive. **A page-owning class is always a package and
never carries one**, and packages are exactly what icon-inheritance chains run through. The fallback:

1. Take the package's 20 px `src` from its **parent's** Package Content row and swap the trailing
   `S.png` for `I.png`.
2. Treat the icon as absent if that file is the library's blank placeholder. The placeholder is
   **calibrated from the document itself**, not by file size — Dymola draws a *different* blank per
   class restriction, which is a deviation from the original design that the evidence forced.

No image decoder is needed.

### Version resolution order

`UsesVersionChecker` needs `ModelNode.Version` on the library root:

1. **The directory-name suffix** — `VeSyMA 2026.1` → `2026.1`. This is the Modelica §13.4 versioned
   directory convention and it is what the *tool* resolves against, so it is the authoritative
   statement of what is on the machine. It is also the only source for libraries shipping no
   `libraryinfo.mos`.
2. **`libraryinfo.mos`** → `version="2.9.0"`, for a directory with no version suffix.
3. Otherwise null — "states no version" is already handled correctly downstream.

The suffix match is **guarded**: accept it only when the text after the final space is version-shaped.
An unsuffixed name falls through rather than being taken literally.

## Documented ≠ complete — resolution must be asymmetric

`FlexibleBodies 2.4.0` ships in both Dymola 2025x and 2026x under the same version string with a
different `package.moe`: its documentation went from **167 classes to 425**, the additions being the
entire `FlexibleBodies.Internal.*` subtree. So **the documented class set is a subset of the real
class set, and the size of that gap is a vendor decision that varies between releases of the same
nominal version.**

- A **hit** is trustworthy. The class exists; its `extends` and icon are as documented. Findings that
  depend on a hit may gate.
- A **miss is not proof of absence.** It must never become a broken-reference error. A reference into
  a documented-but-encrypted namespace that fails to resolve stays at reduced confidence — info at
  most, never gating.

This is the narrow claim: parsing documentation does not move an encrypted namespace wholesale from
the roadmap's state 2 to state 1; it moves the *resolvable part* to state 1 and leaves the remainder
in state 2.

## Format stability — measured, not assumed

Surveyed across 13 installed Dymola releases (2021 → 2026x Refresh 1), using MSL as the common corpus:

| Comparison | Result |
|---|---|
| 2025x → 2025x Refresh 1 (MSL 4.0.0) | help directory **byte-identical** |
| 2026x → 2026x Refresh 1 (MSL 4.1.0) | help directory **byte-identical** |
| 2025x → 2026x generated `<head>` | two added CSS rules, nothing else |
| 2025x → 2026x markup for a sampled class | byte-identical |
| CSS class vocabulary | **identical set of 11** in every version |
| `<h3>` section vocabulary | **identical set of 7** in every version |
| `HTML-Generator` meta, DOCTYPE, `Extends from` phrasing | unchanged back to Dymola 2021 |

Every marker the parser depends on has been stable for six years and thirteen releases; Refresh
releases are structurally no-ops. Still fail **soft** — assert the `HTML-Generator` meta tag, log and
fall back to "external" when expected markers are absent.

**OpenModelica ships nothing equivalent**, for any library, and has no command to generate any. Its
encrypted format is a SEMLA `.mol` archive whose key the vendor must grant to a specific tool. There
is nothing to read, so the feature is Dymola-shaped by necessity rather than by preference.

## Accuracy, and where it is actually checked

Measured against **MSL 4.1.0**, which ships both readable source and generated help, so the
reconstruction can be compared with the truth:

- 6,269 classes recovered, **0 invented**
- 3.4% of source classes undocumented (the protected/hidden ones)
- 2 of 5,129 descriptions differing
- 35 of 4,674 extends lists differing
- **0** classes wrongly reported as having no icon
- across all 53 installed encrypted libraries: 37,686 classes recovered, every synthesized
  declaration parses

**`DocumentationAccuracyTests` needs an installed Dymola and no CI runner has one**, so those figures
are re-measured only on a developer machine that has it. What runs everywhere is the parser's own
suite: 82 fixture-based tests in `ModelicaParser.Tests/ExternalDocs/`, including the two
version-specific facts above. The distinction matters — the parser's *shape* is covered on every
build, its *accuracy against real vendor output* is not. Claiming otherwise was backlog B99.

## Settings live in application settings, not `.mlqt/settings.json`

`AppSettings.ReferenceLibraries` carries the scanned directories and
`UseEncryptedLibraryDocumentation`. **An install location is a property of the machine**: a
colleague's checkout or a CI runner will not have Dymola at the same path, and baking one into a
committed repository settings file breaks it for everyone else. CI supplies the equivalent
explicitly with the CLI's `--dependency`.

`TreatAsExternalNamespaces` was designed and **not built** — it belongs to the Wave-2
confidence-aware resolver, and the case it was for (a library shipping no `help/` at all) is already
handled by that library simply not resolving, which is the pre-existing "assume valid, never gates"
behaviour.

## Free wins worth remembering

- `Resources/` is unencrypted, so registering the stub library as a `LibraryInfo(name, rootPath)`
  makes `modelica://Battery/Resources/…` resolve with no extra work.
- The vendor's real icon PNGs are on disk, so the library browser can render them for stub classes.

## Known limits

- **Class kind is inferred**, not read — a `record` and a `connector` both present as "no tables".
  Kind-sensitive rules (naming conventions, kind-based metrics) must never run on a stub.
- **`partial` is invisible.** A rule objecting to instantiating a partial class cannot run across the
  boundary. Accept and document rather than guessing from the name.
- **Scale.** One library is 1,128 HTML files and a user may point at a whole Dymola library folder —
  ~50 libraries, tens of thousands of classes. Parse in parallel and keep `DocumentedClass`
  allocation-light.
