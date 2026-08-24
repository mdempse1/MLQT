# Encrypted Libraries

Commercial Modelica libraries are usually distributed **encrypted**: the whole library is a single
`package.moe` file with no readable source. MLQT works by reading Modelica source, so a library that
depends on one of these is checked against an incomplete picture — and that produces findings that
are simply wrong:

- every `modelica://Battery.Cells.…` link is reported as a **broken model reference**;
- a class that inherits its icon from an encrypted base class is reported as **missing an Icon
  annotation**;
- checks that resolve types across the boundary have nothing to resolve against.

MLQT solves this using the documentation the vendor already ships. Nearly every encrypted library
includes a `help/` folder of generated HTML describing its classes, and that is enough to answer the
three questions the checks actually ask: **does this class exist**, **what does it extend**, and
**does it have an icon**.

> Nothing is decrypted. MLQT reads the vendor's own published documentation, exactly as a person
> browsing it would.

---

## What an encrypted library looks like

```
Battery 2.9.0/
  package.moe          the encrypted library — unreadable
  libraryinfo.mos      registration script (sometimes present)
  Resources/           NOT encrypted: images, data files, scripts
  help/
    Battery.html                              one HTML file per package
    Battery_BMS_Interfaces.html
    Battery.BMS.Interfaces.CurrentRestrictorI.png
```

MLQT treats any directory containing a `package.moe` as an encrypted library.

## What MLQT recovers, and what it cannot

| Recovered | Not recovered |
|---|---|
| Every documented class and its full name | Equations, algorithms, any implementation |
| Description strings | The declared types of parameters and connectors |
| Base classes (`extends`) | Whether a class is `partial` |
| Whether a class has an icon | The class restriction keyword (`model`, `record`, …) — inferred, not stated |
| Parameter, connector, input and output **names** | Protected and hidden classes — the vendor omits them |
| The library version | |

This is deliberately a partial picture, and MLQT is careful about the difference between *"the
documentation says no"* and *"the documentation does not say"*. Where something is genuinely unknown
it is left unknown rather than guessed at in the direction that would invent a finding.

**Documented is not the same as complete.** A vendor decides how much of a library to document, and
that can change between releases of the same version. So a class that the documentation does not
mention may still exist. MLQT therefore treats a *hit* as reliable but never hardens a *miss* into
certainty.

---

## Using it

### In the application

Add the folder your libraries are installed in under **Settings → Reference Libraries**. For Dymola
that is typically:

```
C:\Program Files\Dymola 2026x Refresh 1\Modelica\Library
```

MLQT scans the folder, finds every library beneath it — encrypted or not — and loads them read-only
at startup, before your own libraries are analysed. The table shows how many libraries each folder
contributes and how many of those are encrypted, so a mistyped or moved path is obvious immediately
rather than surfacing later as unresolved references.

Reference libraries are **never checked, formatted, committed or written to**. They appear in the
library browser so you can read them, and nothing more.

The paths are stored in your application settings rather than in the repository's
`.mlqt/settings.json`, because an install location is a property of your machine — a colleague's
checkout or a CI runner will not have the same one.

### On the command line

Pass the library with `--dependency`, exactly as you would an unencrypted one:

```bash
mlqt check ./MyLibrary --dependency "C:\Program Files\Dymola 2026x Refresh 1\Modelica\Library\Battery 2.9.0"
```

```
note: loaded Battery for reference resolution (not reported on)
No findings in 2 model(s).
```

You can point `--dependency` at a whole folder of libraries and MLQT will load each one it finds.
An encrypted library found *inside* the repository being checked is also loaded for reference, but
is never reported on — there is no source in it to have an opinion about.

### From the MCP server

`load_library` accepts an encrypted library directory and loads it the same way, returning the usual
library summary.

---

## When a library ships no documentation

A few libraries ship no `help/` folder at all. MLQT says so and carries on:

```
warning: encrypted library 'CATIAMultiBody' ships no usable documentation, so its classes cannot be
         recovered; references into it stay unresolved
```

Its namespace stays opaque: references into it are still reported as unresolved, and icons inherited
from it are still invisible. This is the honest outcome — the alternative, treating the library as
empty, would turn every reference into it into a fabricated broken-reference finding.

---

## How accurate is it?

The Modelica Standard Library ships **both** readable source and Dymola-generated documentation, so
the reconstruction can be measured exactly rather than assumed. MLQT's test suite loads MSL 4.1.0
both ways on every build and compares them:

| Measure | Result |
|---|---|
| Classes recovered from documentation | 6269 |
| Recovered classes that do not exist in source | **0** |
| Source classes the documentation omits | 218 (3.4% — the protected and hidden ones) |
| Descriptions compared / differing | 5129 / 2 |
| `extends` lists compared / differing | 4674 / 35 |
| Classes wrongly reported as having **no** icon | **0** |

The last row is the one that matters most: wrongly reporting "no icon" is what makes the icon rule
fire on a class that inherits a perfectly good one, which is the false positive this feature exists
to remove. It is zero, and the test asserts it stays zero.

The reverse direction — documentation showing an icon where the rule would say there is none — does
happen (464 classes). The two are answering slightly different questions: the rule asks whether
there is an `Icon` annotation, while the generator asks whether the class *renders* to anything, which
includes placed sub-components. That direction can only ever suppress a finding on a class whose
source MLQT cannot read, so it is the safe way round.

Across all 53 encrypted libraries installed with Dymola 2026x Refresh 1, MLQT recovers **37,686
classes**, and every one of them produces a declaration that parses.

---

## Which tools are supported

**Dymola** — fully supported. Every commercial library that ships documentation ships it in
Dymola's format, whichever tool the library is ultimately used with. The format has been verified
stable across thirteen Dymola releases (2021 through 2026x Refresh 1): the same section headings,
the same markup, byte-identical output between a release and its Refresh.

**OpenModelica** — not applicable. OpenModelica ships no per-class documentation for any library
and has no command to generate any, so there is nothing to read. Its encrypted format is different
too (a SEMLA `.mol` archive), and reading one requires a key the library vendor must grant to a
specific tool. See [design-encrypted-libraries.md](design-encrypted-libraries.md) for the detail.

---

## Related

- [cli.md](cli.md) — `--dependency` and reference resolution in CI
- [settings-reference.md](settings-reference.md) — all settings
- [code-review.md](code-review.md) — where findings are shown
- [design-encrypted-libraries.md](design-encrypted-libraries.md) — the design and the evidence behind it
