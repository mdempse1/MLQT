# MLQT CLI (`mlqt`)

A headless, cross-platform command-line tool that style-checks a Modelica library and reports
findings. It runs the same checks as the MLQT desktop app, with no UI and no MAUI dependency, so it
works on Windows, Linux, and macOS and is suited to CI pipelines.

## Install

Packaged as a .NET tool:

```bash
dotnet tool install --global MLQT.Cli      # provides the `mlqt` command
# or into an isolated location:
dotnet tool install --tool-path ./tools MLQT.Cli
```

Requires the .NET 10 runtime.

## Usage

```
mlqt check <library-path> [options]
```

`<library-path>` is a Modelica library directory (a package with a `package.mo`, or a flat folder
of `.mo` files) or a single `.mo` file.

| Option | Description | Default |
|--------|-------------|---------|
| `--config <path>` | Settings file to use | `<library-path>/.mlqt/settings.json`, else built-in defaults |
| `--format console\|json\|junit` | Output format | `console` |
| `--out <file>` | Write output to a file instead of stdout | stdout |
| `--fail-on off\|warning\|error` | Exit non-zero when findings reach this level | `error` |
| `--no-color` | Disable coloured console output (also honours `NO_COLOR`) | colour on a TTY |
| `-h`, `--help` | Show help | |

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | No findings at or above `--fail-on` |
| `1` | Findings at or above `--fail-on` |
| `2` | Usage or load error (bad path, unreadable/invalid config) |

Because the built-in rules currently report at **warning** severity, the default `--fail-on error`
is effectively report-only (it surfaces findings but exits `0`). Use `--fail-on warning` for a
strict gate, or `--fail-on off` to never fail.

## Settings

The rules that run are controlled by a `StyleCheckingSettings` JSON file — the same format the
desktop app writes to `<repo>/.mlqt/settings.json`. If no config is found, no rules are enabled and
no findings are produced. See [settings-reference.md](settings-reference.md).

## Output formats

- **console** — human-readable, grouped by file, with a per-severity summary.
- **json** — an object with `tool`, `library`, `modelsChecked`, `findingCount`, and a `findings`
  array. Each finding includes its `Fingerprint` (a stable, reformat-independent identity).
- **junit** — JUnit XML where each finding is a failing test case. This makes findings appear in the
  native test-report UI of most CI systems (TeamCity, Jenkins, GitLab, Azure DevOps) with no extra
  integration — point the CI's test-report step at the file.

### CI examples

```bash
# Fail the build on warnings, and publish findings as a JUnit report
mlqt check ./MyLibrary --fail-on warning --format junit --out mlqt-results.xml
```

```bash
# Machine-readable output for custom processing
mlqt check ./MyLibrary --format json --out findings.json
```
