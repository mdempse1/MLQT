# MLQT.Cli

Loaded when working in `MLQT.Cli/` or `MLQT.Cli.Tests/`. The repository-wide rules are in the root
`CLAUDE.md`; user documentation is `Documentation/cli.md` and `Documentation/ci-quality-gate.md`.

Headless cross-platform `mlqt` CLI, **shipped inside each platform's installer rather than as a
`dotnet tool`** (7b-7: `dotnet tool install` is an SDK command, so packaging it that way obliged a
build agent to install the SDK to run a linter).

- `mlqt check` style-checks a Modelica library and emits console/JSON/JUnit/SARIF/TeamCity/markdown
  output, or a GitHub pull-request review body (`review`), with CI exit codes (0 passed, 1 gate
  failed, 2 usage/setup error or a defect in mlqt — `CliEntry` catches everything), reusing the shared check pipeline in `MLQT.Services/Checking/` (see
  `MLQT.Services/CLAUDE.md`) — change the primitive there, never this tool's path
- `mlqt baseline` manages the accepted-debt file
- `mlqt compare` lists the classes one copy of a library has that another does not, matching on full
  Modelica name so a restructure on disk is not a difference
- `mlqt hook` installs the check as a git pre-commit hook

| File | Purpose |
|------|---------|
| `MLQT.Cli/CliEntry.cs` | Command dispatch, the `--help` text and `ExitCodes`. The help text is a second copy of `Documentation/cli.md`'s option tables: change both |
| `MLQT.Cli/CheckOptions.cs` | `check`'s option parser; `BaselineCommand.cs`, `CompareOptions.cs` and `HookOptions.cs` hold the other commands' |
| `MLQT.Cli/CheckPipeline.cs` / `CheckRunner.cs` | The CLI's load+check, and the gate/report/exit-code layer over it |
| `MLQT.Cli/SettingsResolver.cs` / `RepoPath.cs` | The upward `.mlqt` walk (settings, dictionary, metrics history), and how relative `--config`/`--baseline`/`--sarif-base`/`--metrics-out` resolve against the library |
| `MLQT.Cli/Formatting/` | One `IFindingFormatter` per output format, all over one `CheckReport` |

**SARIF fixture:** `TestFixtures/SarifSmoke/` is a deliberately imperfect Modelica library committed at
a **nested** path (`Libraries/Smoke`). `build/validate-sarif.ps1` checks a report generated from it
against the SARIF 2.1.0 schema with `Sarif.Multitool`, and the nesting is what proves `--sarif-base`
writes paths a consumer can resolve. Run on every push by `build-and-test.yml`; run it locally the
same way.

**Documented commands are tested commands.** `DocumentedCommandTests` collects every `mlqt check|baseline|compare|hook`
line inside a fenced block in `Documentation/` and `Design/` and fails unless some `Cli.Run` in this
suite passes a superset of its flags — a new example with a new flag combination needs a test.
`RuleDocumentationTests` holds `cli.md` to carrying every diagnostic id and a `## Diagnostics` heading,
which the SARIF `helpUri` links to.

`MarkdownTableTests` lives in this test project and guards the tables in `Design/`, every `CLAUDE.md`
and every skill — see the root `CLAUDE.md`.
