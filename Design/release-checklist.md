# Release checklist

What CI cannot do for you, and why each item is here. Everything else is already a gate on every
push — this is only the work that needs a machine, a tool or a decision that no runner has.

**Run these in order - except §7, which belongs before tagging. Each one says what it proves and
what it costs.**

---

## 1. The full suite, not the seven CI runs

```powershell
./build/run-all-tests.ps1
```

CI runs seven suites whole and two in part. This runs all ten whole. What it adds is the whole reason
the script exists: the classes of `DymolaInterface.Tests` and `OpenModelicaInterface.Tests` marked
`[Trait("Requires", ...)]` need a live tool that no runner has, and
`MLQT.Journeys` needs `pwsh MLQT.Journeys/bin/Release/net10.0/playwright.ps1 install chromium` once.

**A failure is a failure**, including in the tool-dependent suites. There is no known exception:
B116, which this list once excused against `omc` 1.26, was a test that was wrong about omc and was
fixed on 2026-09-08. Anything red is a finding.

## 2. The fidelity corpus, by hand

```powershell
$env:MLQT_FIDELITY_CORPUS = "C:\Projects\Modelica\ModelicaStandardLibrary;C:\Projects\Modelica\Modelica-Buildings-Original"
dotnet test ModelicaParser.Tests --filter "FullyQualifiedName~RoundTripsOverAWholeLibrary"
```

The strongest assertion in the repository — strip the highlight tags and what comes back is the
source, character for character — over 8,367 files and 1.1M lines. **With the variable unset the
test returns immediately**, so an ordinary run says nothing about fidelity, which is the trap rather
than the convenience.

**Required whenever the release carries a change to `ModelicaTokenClassifier`.** Worth running
anyway: it is the only thing here that has ever seen a real library.

What it does *not* cover, so that nobody assumes it does: it calls the catching `Highlight(string)`
and strips, and `Plain` — the fallback — round-trips too. A fault that makes the emit loop throw
passes this test at any corpus size. That is what `TheEmitLoopIsFaithfulAndStillColours` is for, and
it runs on every push (B234).

## 3. The resolution corpus, by hand

```powershell
$msl = "C:\Projects\Modelica\ModelicaStandardLibrary"
$env:MLQT_RESOLUTION_CORPUS = "$msl\Modelica;$msl\ModelicaServices;$msl\Complex.mo;C:\Projects\Modelica\Modelica-Buildings\Buildings"
dotnet test ModelicaGraph.Tests --filter "FullyQualifiedName~ResolutionCorpusTests"
```

The two facts the unit-consistency design stands on, over MSL 4.1.0 and Buildings 13 loaded into one
graph: **every component type resolves** (124,977 of them on 2026-10-03), and **a package redeclared on
an instance gives every member the unit the constraining type does** (26,145 members under 6,239
redeclarations). About a minute. **With the variable unset both tests return immediately.**

Name each library's own directory, not a repository root: MSL's holds `ModelicaTestConversion4`,
written against 3.2.3, whose names rightly do not resolve against 4.1.0. Buildings 13 declares MSL
4.0.0 and the figures above are for it beside 4.1.0, the version on the development machine; the
tests load whatever they are given and do not check that a library's `uses` matches.

**Required whenever the release carries a change to `TypeResolver`, `ClassElementResolver` or
`UnitResolver`.** Taking away the lookup among inherited classes (`inherited: false` in
`ResolveWithInheritance`) fails both: 1,744 types stop resolving and 813 members disagree.

## 4. The nightly WebKit rehearsal, triggered deliberately

```bash
gh workflow run nightly-webkit.yml
```

WebKitGTK is what the Linux desktop host runs on, and this is the only job that exercises it. It is
scheduled rather than push-triggered, so **a change to the runner image or the host is unproven
here until the next night** — and the runners are pinned to `ubuntu-24.04` precisely because
Playwright ships no WebKit for 26.04 (B256). Trigger it and wait for it.

## 5. Tag, then watch the release workflow's Linux half

`release.yml` only fires on a tag, so its `.deb` job is the least-rehearsed thing in the repository
and does the most environment-dependent work: `apt-get install`, `xvfb`, and the 16 `/selftest`
probes against the package it has just extracted. **That job passing is what "the installer works"
means here** — `build/package-deb.sh` exists to prove it rather than to produce a file.

The Windows half is better covered, because `desktop-selftest` runs the same probes on every push.

## 6. Read the coverage ledger, do not just pass the gate

```powershell
./build/check-coverage.ps1
```

Passing is necessary and says little: it is a ratchet over accepted debt. Before a release, read
`build/coverage-baseline.json` for entries whose *reason* has gone stale — a number cited in the
prose that no longer matches the recorded one is a reason nobody has read since it was written.

**Do not re-record from a machine with an svn client.** `-UpdateBaseline` declines to raise a
`RevisionControl.Svn*` figure when one is present and names what it held back, because the Code
Coverage job has no svn and a figure from here is one CI cannot reach (B266, and again in B191).

## 7. Protect the MCP stack, and compare it with the plain one

**Do this before tagging (§5).** Consumers ship the MCP stack protected by an obfuscator at its
strongest settings (B502), so a release is proven against such a build, configured the hardest way
a consumer does:

- **Merged**: the seven stack assemblies merged into the server's own, the library DLLs deleted, and
  `deps.json` cut to match - or the host refuses to start for want of them.
- **Renaming everything**: public names, all parameters, unprintable names, and public types
  internalized - the setting that deletes enum members from metadata (measured: 7 of 44 enums left
  with none). With string encryption, control flow at its highest level, method-body encryption,
  code virtualization and call hiding.
- **Anti-tamper off.** It has been measured producing builds that intermittently fail their own
  integrity check at start-up (3 of 12 trials, from identical input), which would read here as a
  regression.

Then run the same scripted session against the plain build and the protected one. It needs Python 3
and `git` (it makes the fixture copy a git working copy), and writes `<out>.timings.json` beside each
transcript:

```powershell
python build/obfuscation/mcp_session.py <plain>\MLQT.McpServer.exe TestFixtures\SarifSmoke plain.json
python build/obfuscation/mcp_session.py <protected>\MLQT.McpServer.exe TestFixtures\SarifSmoke protected.json
```

**The two transcripts must be identical**: the instructions, every tool's input schema, the result
of a call to each of the 66 tools - edits as previews, then real writes and a reload, a wired diagram
image, a routed connection, change impact over git - and the `.mlqt/settings.json` the session
wrote. A difference is decoded with the obfuscator's mapping file. **Judge the build by running it,
not by inspecting it**: a protected assembly's metadata on disk can be emptied (parameter names) and
scrambled (resource names) and restored as it loads.

To time a large library with each build, where control flow and string encryption on the parser show:

```powershell
python build/obfuscation/mcp_session.py <protected>\MLQT.McpServer.exe --time-check C:\Projects\Modelica\ModelicaStandardLibrary\Modelica protected-time.json
```

For reference (2026-10-14, MSL 4.1): a load took 123-210% of the plain time and a check 135-169%;
the first call into protected code paid about a second once; and a wired diagram image took 3.8 s
against 0.24 s. If a consumer finds the load too slow, the generated ANTLR parser (partial classes,
so an attribute can be added beside them) is where to exclude control flow first.

**Required whenever the release carries a change to a JSON contract, an MCP tool's parameters, or
anything `CODING_GUIDELINES.md` §Names That Must Survive Obfuscation covers.** It needs a licence for
the obfuscator, which is why it is here and not in CI.

---

## What this list is not

It is not a substitute for the gates on every push — the seven suites, the coverage ratchet and the
SARIF validation run there and are not repeated here. Adding something to this list is a decision
that it *cannot* run there; if it can, it belongs in CI instead.
