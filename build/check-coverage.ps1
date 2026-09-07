<#
.SYNOPSIS
    Merges every test project's coverage and gates on it, per class, against a committed baseline.

.DESCRIPTION
    CLAUDE.md sets a bar - >80% line coverage per class, >95% for ModelicaParser - and until now
    nothing checked it. CI collected coverage from three of the six suites, printed a number, and
    moved on, so the bar was unverified for exactly the assemblies the CI/CD work added, and the
    94.5% quoted for MLQT.Cli in a review was a local figure nothing reproduced.

    Two things make a naive gate the wrong tool here, and both are why this one has a baseline:

      - Not every suite can run everywhere. The SVN tests need a working copy and a server no runner
        has, so on CI the SVN classes in RevisionControl sit near zero. That is a fact about the
        runner, not about the code, and a gate that fails on it teaches people to ignore the gate.
      - Some real debt predates the bar. DymolaCheckingService and OpenModelicaCheckingService are
        around 32% because they talk to a live tool.

    So this is a ratchet, which is the same answer MLQT gives its own users: today's numbers are
    recorded in build/coverage-baseline.json, and the build fails when a class goes backwards from
    what is recorded, or when a class that met the bar stops meeting it, or when a new class arrives
    below it. Debt is tolerated; new debt is not. Run with -UpdateBaseline to re-record, and read the
    diff - it is the point of keeping the file in the repository.

    What is deliberately NOT gated:

      - Classes below MinimumLines coverable lines. A four-line record whose only uncovered lines are
        the compiler's own Equals/GetHashCode reads as 50%, and chasing that number produces tests
        that assert nothing. They are still measured, printed and counted.
      - Generated code, which nobody wrote and nobody can sensibly test to a bar: ANTLR's output from
        modelica.g4 (modelicaParser and friends - 4,862 coverable lines of it, which on its own moves
        the assembly's average by more than any real class can) and the regex source generator's.
      - DymolaInterface and OpenModelicaInterface, whose tests drive a live install, and MLQT.Shared,
        which has no tests at all until phase 7a builds the harness.

.PARAMETER Configuration
    Build configuration to test. Defaults to Release, matching CI - and it has to match: Release
    optimises differently, so the same code measures a point or two apart in Debug and a baseline
    recorded from one configuration produces spurious failures against the other.

.PARAMETER UpdateBaseline
    Re-record the baseline from this run instead of gating. Review the diff before committing.

.PARAMETER SkipTests
    Reuse the coverage already in -ResultsDirectory rather than running the suites again.

.PARAMETER MinimumLines
    Smallest class, in coverable lines, that is gated. Below this the percentage is noise.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $UpdateBaseline,
    [switch] $SkipTests,
    [string] $ResultsDirectory = 'CoverageResults',
    [string] $ReportDirectory = 'CoverageReport',
    [string] $BaselinePath = 'build/coverage-baseline.json',
    [int]    $MinimumLines = 25
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

# The bar per assembly. ModelicaParser is higher because CLAUDE.md calls it critical to the project:
# everything downstream is built on what it parses, so a gap there is a gap in every other number.
$bars = @{
    'ModelicaParser' = 95.0
    'ModelicaGraph'  = 80.0
    'MLQT.Services'  = 80.0
    'MLQT.McpServer' = 80.0
    'RevisionControl' = 80.0
    'mlqt'           = 80.0   # the assembly name of MLQT.Cli, from its ToolCommandName
}

# The suites, and the filter each needs. SVN integration tests want a working copy at
# C:\Projects\ModelicaEditorTest plus a server; the build workflow excludes them the same way, so
# this has to as well or the local numbers and CI's would not be comparable.
$suites = @(
    @{ Project = 'ModelicaParser.Tests';  Filter = $null }
    @{ Project = 'ModelicaGraph.Tests';   Filter = $null }
    @{ Project = 'MLQT.Services.Tests';   Filter = $null }
    @{ Project = 'MLQT.Cli.Tests';        Filter = $null }
    @{ Project = 'MLQT.McpServer.Tests';  Filter = $null }
    @{ Project = 'RevisionControl.Tests'; Filter = 'FullyQualifiedName!~Svn' }
)

function Fail([string] $message) {
    Write-Host "FAIL: $message" -ForegroundColor Red
    Pop-Location
    exit 1
}

if (-not $SkipTests) {
    if (Test-Path $ResultsDirectory) { Remove-Item $ResultsDirectory -Recurse -Force }

    foreach ($suite in $suites) {
        Write-Host "Collecting coverage: $($suite.Project)" -ForegroundColor Cyan
        $arguments = @(
            'test', $suite.Project, '-c', $Configuration, '--no-build', '--nologo', '-v', 'q',
            '--collect:XPlat Code Coverage',
            '--results-directory', (Join-Path $ResultsDirectory $suite.Project)
        )
        if ($suite.Filter) { $arguments += @('--filter', $suite.Filter) }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { Fail "$($suite.Project) did not pass; coverage from a failed run means nothing" }
    }
}

$reports = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue
if ($reports.Count -lt $suites.Count) {
    # The trap this guards: a suite that produced no report is not 0% coverage, it is no information,
    # and merging it in silently drags every class it owns to zero.
    Fail "expected $($suites.Count) coverage reports, found $($reports.Count). A suite produced nothing, and a missing report reads as 0%"
}

Write-Host "Merging $($reports.Count) coverage reports" -ForegroundColor Cyan
& reportgenerator `
    "-reports:$ResultsDirectory/**/coverage.cobertura.xml" `
    "-targetdir:$ReportDirectory" `
    '-reporttypes:JsonSummary;TextSummary;HtmlSummary' `
    '-assemblyfilters:-DymolaInterface;-OpenModelicaInterface;-MLQT.Shared' `
    '-classfilters:-System.Text.RegularExpressions.Generated*;-modelicaParser;-modelicaLexer;-modelicaBaseListener;-modelicaBaseVisitor*' | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'reportgenerator failed' }

# What -UpdateBaseline writes for a new entry, and what the gate refuses to accept.
$NeedsReason = 'TODO: why is this accepted?'

$summary = Get-Content (Join-Path $ReportDirectory 'Summary.json') -Raw | ConvertFrom-Json

$missing = $bars.Keys | Where-Object { $_ -notin $summary.coverage.assemblies.name }
if ($missing) { Fail "no coverage at all for: $($missing -join ', '). Same trap as above, one level up" }

# Everything gated, with the bar it has to meet.
$gated = foreach ($assembly in $summary.coverage.assemblies) {
    if (-not $bars.ContainsKey($assembly.name)) { continue }
    foreach ($class in $assembly.classesinassembly) {
        [pscustomobject]@{
            Key      = "$($assembly.name)::$($class.name)"
            Assembly = $assembly.name
            Class    = $class.name
            Coverage = [double] $class.coverage
            Lines    = [int] $class.coverablelines
            Bar      = [double] $bars[$assembly.name]
        }
    }
}

$small = $gated | Where-Object { $_.Lines -lt $MinimumLines -and $_.Coverage -lt $_.Bar }
$gated = $gated | Where-Object { $_.Lines -ge $MinimumLines }
$below = $gated | Where-Object { $_.Coverage -lt $_.Bar } | Sort-Object Coverage

# Reasons already recorded, so -UpdateBaseline carries them forward instead of erasing them. A ledger
# entry has to say why it is accepted - that is what makes accepting debt a decision rather than a
# keystroke - and the reason is the only part a person writes.
$reasons = @{}
if (Test-Path $BaselinePath) {
    $existing = Get-Content $BaselinePath -Raw | ConvertFrom-Json
    foreach ($property in $existing.classes.PSObject.Properties) {
        if ($property.Value.PSObject.Properties.Name -contains 'reason') {
            $reasons[$property.Name] = [string] $property.Value.reason
        }
    }
}

if ($UpdateBaseline) {
    $entries = [ordered] @{}
    foreach ($item in ($below | Sort-Object Key)) {
        $entries[$item.Key] = [ordered] @{
            coverage = [math]::Round($item.Coverage, 1)
            lines    = $item.Lines
            bar      = $item.Bar
            reason   = if ($reasons.ContainsKey($item.Key)) { $reasons[$item.Key] } else { $NeedsReason }
        }
    }
    $payload = [ordered] @{
        '_comment'   = 'Classes below the coverage bar, accepted as existing debt. Every entry carries a reason, and the build fails on one that does not - accepting debt is a decision, not a keystroke. The build also fails if a class goes further backwards, or if a class arrives below the bar and is not listed here. Regenerate with build/check-coverage.ps1 -UpdateBaseline, then write a reason for anything new and review the diff.'
        minimumLines = $MinimumLines
        bars         = $bars
        classes      = $entries
    }
    $payload | ConvertTo-Json -Depth 5 | Set-Content $BaselinePath -Encoding utf8
    Write-Host "Recorded $($entries.Count) class(es) in $BaselinePath" -ForegroundColor Green

    $unexplained = @($entries.Keys | Where-Object { $entries[$_].reason -eq $NeedsReason })
    if ($unexplained.Count -gt 0) {
        Write-Host ''
        Write-Host "$($unexplained.Count) new entr(y/ies) need a reason before the build will pass:" -ForegroundColor Yellow
        foreach ($key in $unexplained) { Write-Host "  $key" -ForegroundColor Yellow }
    }

    Pop-Location
    exit 0
}

if (-not (Test-Path $BaselinePath)) { Fail "no coverage baseline at $BaselinePath (create one with -UpdateBaseline)" }
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json
$accepted = @{}
foreach ($property in $baseline.classes.PSObject.Properties) { $accepted[$property.Name] = [double] $property.Value.coverage }

# Half a point of slack. Coverage moves a little when an unrelated edit changes a class's line count,
# and a build that fails on that teaches people to re-record the baseline without reading it.
$tolerance = 0.5

$regressed = @()
$newDebt   = @()
foreach ($item in $below) {
    if ($accepted.ContainsKey($item.Key)) {
        if ($item.Coverage -lt $accepted[$item.Key] - $tolerance) {
            $regressed += [pscustomobject]@{ Item = $item; Was = $accepted[$item.Key] }
        }
    }
    else { $newDebt += $item }
}

$recovered = $accepted.Keys | Where-Object { $_ -notin $below.Key }

Write-Host ''
Write-Host ("Line coverage: {0}%  ({1} classes gated, {2} below their bar)" -f `
    $summary.summary.linecoverage, $gated.Count, $below.Count)
foreach ($assembly in $summary.coverage.assemblies | Where-Object { $bars.ContainsKey($_.name) }) {
    Write-Host ("  {0,-16} {1,6}%   (bar {2}% per class)" -f $assembly.name, $assembly.coverage, $bars[$assembly.name])
}
if ($small.Count -gt 0) {
    Write-Host ("  {0} class(es) under {1} coverable lines are measured but not gated - see the script header" -f $small.Count, $MinimumLines) -ForegroundColor DarkGray
}

if ($recovered) {
    Write-Host ''
    Write-Host 'These now meet the bar and can be dropped from the baseline (-UpdateBaseline):' -ForegroundColor Green
    foreach ($key in ($recovered | Sort-Object)) { Write-Host "  $key" -ForegroundColor Green }
}

if ($newDebt) {
    Write-Host ''
    Write-Host 'Below the bar and not accepted as existing debt:' -ForegroundColor Red
    foreach ($item in $newDebt) {
        Write-Host ("  {0,-70} {1,5}%  (bar {2}%, {3} lines)" -f $item.Key, $item.Coverage, $item.Bar, $item.Lines) -ForegroundColor Red
    }
}

if ($regressed) {
    Write-Host ''
    Write-Host 'Accepted debt that got worse:' -ForegroundColor Red
    foreach ($entry in $regressed) {
        Write-Host ("  {0,-70} {1,5}% (was {2}%)" -f $entry.Item.Key, $entry.Item.Coverage, $entry.Was) -ForegroundColor Red
    }
}

# An entry with no reason is debt nobody decided to take on. Six of them arrived that way when the
# ledger was first recorded - ordinary in-process classes sitting beside the ones that genuinely
# cannot be tested on a runner, indistinguishable from them, and none of them ever asked about again.
$unexplained = @($baseline.classes.PSObject.Properties | Where-Object {
    $_.Value.PSObject.Properties.Name -notcontains 'reason' -or
    [string]::IsNullOrWhiteSpace($_.Value.reason) -or
    $_.Value.reason -eq $NeedsReason
})
if ($unexplained.Count -gt 0) {
    Write-Host ''
    Write-Host 'Accepted as debt with no reason recorded:' -ForegroundColor Red
    foreach ($entry in ($unexplained | Sort-Object Name)) { Write-Host "  $($entry.Name)" -ForegroundColor Red }
}

if ($newDebt -or $regressed) {
    Write-Host ''
    Fail 'coverage went backwards. Add tests, or - if the drop is deliberate and understood - re-record with -UpdateBaseline and explain it in the commit message'
}

if ($unexplained.Count -gt 0) {
    Write-Host ''
    Fail "$($unexplained.Count) baseline entr(y/ies) carry no reason. Write one in $BaselinePath saying why the class is below its bar - 'needs a working SVN server' and 'nobody has written the tests yet' are both fine, and are different facts"
}

Write-Host ''
Write-Host 'Coverage gate passed.' -ForegroundColor Green
Pop-Location
exit 0
