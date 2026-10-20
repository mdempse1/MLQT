# MLQT.Cli

Loaded when working in `MLQT.Cli/` or `MLQT.Cli.Tests/`. The repository-wide rules are in the root
`CLAUDE.md`; user documentation is `Documentation/cli.md` and `Documentation/ci-quality-gate.md`.

Headless cross-platform `mlqt` CLI, **shipped inside each platform's installer rather than as a
`dotnet tool`** (7b-7: `dotnet tool install` is an SDK command, so packaging it that way obliged a
build agent to install the SDK to run a linter).

- `mlqt check` style-checks a Modelica library and emits console/JSON/JUnit/SARIF/TeamCity/markdown
  output with CI exit codes, reusing the shared check pipeline in `MLQT.Services/Checking/` (see
  `MLQT.Services/CLAUDE.md`) — change the primitive there, never this tool's path
- `mlqt baseline` manages the accepted-debt file
- `mlqt compare` lists the classes one copy of a library has that another does not, matching on full
  Modelica name so a restructure on disk is not a difference
- `mlqt hook` installs the check as a git pre-commit hook

| File | Purpose |
|------|---------|
| `MLQT.Cli/CheckPipeline.cs` / `CheckRunner.cs` | The CLI's load+check, and the gate/report/exit-code layer over it |

**SARIF fixture:** `TestFixtures/SarifSmoke/` is a deliberately imperfect Modelica library committed at
a **nested** path (`Libraries/Smoke`). `build/validate-sarif.ps1` checks a report generated from it
against the SARIF 2.1.0 schema with `Sarif.Multitool`, and the nesting is what proves `--sarif-base`
writes paths a consumer can resolve. Run on every push by `build-and-test.yml`; run it locally the
same way.

`MarkdownTableTests` lives in this test project and guards the tables in `Design/`, every `CLAUDE.md`
and every skill — see the root `CLAUDE.md`.
