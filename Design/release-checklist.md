# Release checklist

What CI cannot do for you, and why each item is here. Everything else is already a gate on every
push — this is only the work that needs a machine, a tool or a decision that no runner has.

**Run these in order. Each one says what it proves and what it costs.**

---

## 1. The full suite, not the seven CI runs

```powershell
./build/run-all-tests.ps1
```

CI runs seven suites. This runs ten. The three it adds are the whole reason the script exists:
`DymolaInterface.Tests` and `OpenModelicaInterface.Tests` need a live tool that no runner has, and
`MLQT.Journeys` needs `pwsh MLQT.Journeys/bin/Release/net10.0/playwright.ps1 install chromium` once.

**A failure is a failure**, including in the tool-dependent suites. B116 is open against `omc` 1.26
and is the one known exception; anything else is a finding.

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

## 3. The nightly WebKit rehearsal, triggered deliberately

```bash
gh workflow run nightly-webkit.yml
```

WebKitGTK is what the Linux desktop host runs on, and this is the only job that exercises it. It is
scheduled rather than push-triggered, so **a change to the runner image or the host is unproven
here until the next night** — and the runners are pinned to `ubuntu-24.04` precisely because
Playwright ships no WebKit for 26.04 (B256). Trigger it and wait for it.

## 4. Tag, then watch the release workflow's Linux half

`release.yml` only fires on a tag, so its `.deb` job is the least-rehearsed thing in the repository
and does the most environment-dependent work: `apt-get install`, `xvfb`, and the 16 `/selftest`
probes against the package it has just extracted. **That job passing is what "the installer works"
means here** — `build/package-deb.sh` exists to prove it rather than to produce a file.

The Windows half is better covered, because `desktop-selftest` runs the same probes on every push.

## 5. Read the coverage ledger, do not just pass the gate

```powershell
./build/check-coverage.ps1
```

Passing is necessary and says little: it is a ratchet over accepted debt. Before a release, read
`build/coverage-baseline.json` for entries whose *reason* has gone stale — a number cited in the
prose that no longer matches the recorded one is a reason nobody has read since it was written.

**Do not re-record from a machine with an svn client.** `-UpdateBaseline` declines to raise a
`RevisionControl.Svn*` figure when one is present and names what it held back, because the Code
Coverage job has no svn and a figure from here is one CI cannot reach (B266, and again in B191).

---

## What this list is not

It is not a substitute for the gates on every push — the seven suites, the coverage ratchet and the
SARIF validation run there and are not repeated here. Adding something to this list is a decision
that it *cannot* run there; if it can, it belongs in CI instead.
