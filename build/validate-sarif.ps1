<#
.SYNOPSIS
    Generates a SARIF report from the committed smoke fixture and validates it against the
    SARIF 2.1.0 schema.

.DESCRIPTION
    Phase 4 recorded "validate SARIF output against the 2.1.0 shape in tests" as a risk and then
    closed it by inspection: the CLI tests assert the shape *we* expect, which cannot catch a
    document that is well-formed by our lights and rejected by a consumer's. This script is that
    validation, and CI runs it on every push.

    It checks three things, because a schema-valid document can still be useless:

      1. `sarif validate` reports no errors and no warnings. Warnings are treated as failures on
         purpose  -  the report is clean today, and the cheapest moment to fix one is the build that
         introduces it. Relax this if a future rule proves too opinionated.
      2. The file paths are relative to the repository root, not to the library. The fixture lives at
         TestFixtures/SarifSmoke/Libraries/Smoke, so a report that named `package.mo` would be one a
         consumer resolves against nothing.
      3. Some results arrived. The fixture is deliberately imperfect; an empty report would mean it
         had quietly stopped testing anything.

.PARAMETER OutputPath
    Where to write the generated report. Defaults to a temporary file, which is removed afterwards.

.PARAMETER Configuration
    Build configuration for the CLI. Defaults to Release.

.EXAMPLE
    pwsh build/validate-sarif.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputPath,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repoRoot 'TestFixtures/SarifSmoke'
$library = Join-Path $fixtureRoot 'Libraries/Smoke'
$config = Join-Path $fixtureRoot '.mlqt/settings.json'

$temporary = -not $OutputPath
if ($temporary) { $OutputPath = Join-Path ([System.IO.Path]::GetTempPath()) "mlqt-smoke-$([guid]::NewGuid().ToString('N')).sarif" }

Write-Host "Building the CLI ($Configuration)..."
dotnet build (Join-Path $repoRoot 'MLQT.Cli/MLQT.Cli.csproj') -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "building MLQT.Cli failed" }

$mlqt = Join-Path $repoRoot "MLQT.Cli/bin/$Configuration/net10.0/mlqt.exe"
if (-not (Test-Path $mlqt)) { throw "mlqt not found at $mlqt" }

Write-Host "Checking the smoke fixture..."
# The fixture is meant to produce findings, so --fail-on off: a non-zero exit here would mean the
# check itself failed to run, which is a different thing from the library being imperfect.
& $mlqt check $library --config $config --format sarif --sarif-base $repoRoot --out $OutputPath --fail-on off
if ($LASTEXITCODE -ne 0) { throw "mlqt check failed with exit code $LASTEXITCODE" }

$report = Get-Content $OutputPath -Raw | ConvertFrom-Json

# --- 2. paths a consumer can resolve --------------------------------------------------------------
$expected = 'TestFixtures/SarifSmoke/Libraries/Smoke/package.mo'
$uris = @($report.runs[0].results | ForEach-Object { $_.locations[0].physicalLocation.artifactLocation.uri } | Sort-Object -Unique)
if ($uris -notcontains $expected) {
    throw "SARIF paths are not relative to the repository root. Expected '$expected', got: $($uris -join ', ')"
}

# --- 3. the fixture is still testing something ----------------------------------------------------
$resultCount = @($report.runs[0].results).Count
if ($resultCount -eq 0) { throw "the smoke fixture produced no findings  -  it has stopped testing anything" }
Write-Host "  $resultCount result(s), paths relative to the repository root."

# --- 1. the schema ---------------------------------------------------------------------------------
if (-not (Get-Command sarif -ErrorAction SilentlyContinue)) {
    Write-Host "Installing Sarif.Multitool..."
    dotnet tool install --global Sarif.Multitool | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "installing Sarif.Multitool failed" }

    # A tool installed during this session is not on the PATH this process inherited.
    $toolPath = Join-Path $HOME '.dotnet/tools'
    if (Test-Path $toolPath) { $env:PATH = "$toolPath$([System.IO.Path]::PathSeparator)$env:PATH" }
    if (-not (Get-Command sarif -ErrorAction SilentlyContinue)) {
        throw "Sarif.Multitool installed but 'sarif' is not on the PATH ($toolPath)"
    }
}

Write-Host "Validating against the SARIF 2.1.0 schema..."
$validation = & sarif validate $OutputPath 2>&1
$validation | Out-Host
if ($LASTEXITCODE -ne 0) { throw "sarif validate reported errors" }

# `sarif validate` exits 0 for warnings, and a warning is how a consumer's requirement first shows up.
$diagnostics = @($validation | Where-Object { $_ -match ':\s*(error|warning)\s+SARIF\d+' })
if ($diagnostics.Count -gt 0) {
    throw "sarif validate reported $($diagnostics.Count) diagnostic(s)  -  see above"
}

if ($temporary) { Remove-Item $OutputPath -Force -ErrorAction SilentlyContinue }
Write-Host "SARIF output is valid." -ForegroundColor Green
