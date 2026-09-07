# SARIF smoke fixture

A deliberately imperfect Modelica library, committed at a **nested path** so a SARIF report generated
from it exercises two things at once:

- the document validates against the SARIF 2.1.0 schema (`sarif validate`), which is what
  [build/validate-sarif.ps1](../../build/validate-sarif.ps1) runs and CI enforces on every push;
- the file paths in it are written relative to the **repository root** rather than the library, which
  is what a consumer resolves them against. That only shows up when the library is *not* the
  repository root — hence `Libraries/Smoke` rather than a library at the top.

The library is small on purpose: it needs to produce a handful of findings across a few rules, not to
be a realistic library. `.mlqt/settings.json` beside it enables the rules that make it do so, and is
passed with `--config`.

Findings are expected here. A run that reported none would mean the fixture had stopped testing
anything, so the script checks that some arrived.
