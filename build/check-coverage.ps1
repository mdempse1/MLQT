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

    A fourth way it fails, and the reason the baseline has an "excluded" list: a class in the ledger
    that is not in the report at all. That is not the same fact as "it meets the bar now" - it is no
    information - and until B104 the gate said the same sentence for both, so debt could be paid off
    by ceasing to be measured. The xUnit v3 migration made it happen: MLQT.McpServer::Program is
    top-level statements, so compiler-generated, and coverlet.MTP excludes generated code where
    coverlet.collector had measured it at 0%. That exclusion is right - see below - but nothing
    distinguished it from a class that stopped being measured for a bad reason. Deliberate ones are
    now listed under "excluded" with a reason each, the same rule the debt entries follow, and the
    gate also fails if an excluded class starts being measured again, since leaving it listed would
    hide a later regression in it.

    What is deliberately NOT gated:

      - Classes below MinimumLines coverable lines. A four-line record whose only uncovered lines are
        the compiler's own Equals/GetHashCode reads as 50%, and chasing that number produces tests
        that assert nothing. They are still measured, printed and counted.
      - Generated code, which nobody wrote and nobody can sensibly test to a bar: ANTLR's output from
        modelica.g4 (modelicaParser and friends - 4,862 coverable lines of it, which on its own moves
        the assembly's average by more than any real class can) and the regex source generator's.
      - DymolaInterface and OpenModelicaInterface, whose tests drive a live install.

    MLQT.Shared joined the gate in phase 7a-5, and deliberately with no file filter. The plan for that
    step assumed a Razor component's generated BuildRenderTree would be attributed to the component's
    class and swamp it, and said to measure before choosing. Measured: it is not counted at all -
    MainLayout reports 1,071 coverable lines against a 1,898-line code-behind, which is its C# and
    nothing else. Adding -filefilters:-*.razor would have changed the assembly by 0.1 point and
    removed five classes from the report entirely: the components that kept an @code block, whose
    code lives in a .razor file. A class that is not measured reads as one that is fine, which is
    exactly what B104 was about, so everything is measured instead.

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
    'MLQT.Shared'    = 80.0   # joined the gate in phase 7a-5; see the note in the header
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
    @{ Project = 'MLQT.Shared.Tests';     Filter = $null }
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
        # Microsoft.Testing.Platform, not VSTest: the .NET 10 SDK refuses to run an xUnit v3
        # project through the VSTest target at all. coverlet.MTP is the same instrumentation the
        # coverlet.collector data collector used to run, so the per-class numbers stay comparable
        # with the baseline recorded before the migration. --nologo and -v are VSTest options and
        # are errors here.
        $arguments = @(
            'test', $suite.Project, '-c', $Configuration, '--no-build',
            '--coverlet', '--coverlet-output-format', 'cobertura',
            '--results-directory', (Join-Path $ResultsDirectory $suite.Project)
        )
        if ($suite.Filter) { $arguments += @('--filter', $suite.Filter) }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { Fail "$($suite.Project) did not pass; coverage from a failed run means nothing" }
    }
}

$reports = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura*.xml' -ErrorAction SilentlyContinue
if ($reports.Count -lt $suites.Count) {
    # The trap this guards: a suite that produced no report is not 0% coverage, it is no information,
    # and merging it in silently drags every class it owns to zero.
    Fail "expected $($suites.Count) coverage reports, found $($reports.Count). A suite produced nothing, and a missing report reads as 0%"
}

Write-Host "Merging $($reports.Count) coverage reports" -ForegroundColor Cyan
& reportgenerator `
    "-reports:$ResultsDirectory/**/coverage.cobertura*.xml" `
    "-targetdir:$ReportDirectory" `
    '-reporttypes:JsonSummary;TextSummary;HtmlSummary' `
    '-assemblyfilters:-DymolaInterface;-OpenModelicaInterface' `
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

# Every class the merged report actually measured, captured before the small-class filter below
# takes some of them out of the gate. A baseline entry that is not in here was not measured at
# all, and that is a different fact from "it now meets its bar" - see the vanished check (B104).
$measured = [System.Collections.Generic.HashSet[string]]::new([string[]] @($gated.Key))

$small = $gated | Where-Object { $_.Lines -lt $MinimumLines -and $_.Coverage -lt $_.Bar }
$gated = $gated | Where-Object { $_.Lines -ge $MinimumLines }
$below = $gated | Where-Object { $_.Coverage -lt $_.Bar } | Sort-Object Coverage

# Reasons already recorded, so -UpdateBaseline carries them forward instead of erasing them. A ledger
# entry has to say why it is accepted - that is what makes accepting debt a decision rather than a
# keystroke - and the reason is the only part a person writes.
$reasons = @{}

# Classes the report cannot measure and nobody expects it to. Recorded the same way debt is, with a
# reason each, because "not measured" is a decision too - and because without this list a class can
# leave the ledger simply by ceasing to be measured, which is what B104 was.
$excludedReasons = @{}

# What the ledger accepted last time, needed by -UpdateBaseline to tell a class that was fixed from
# one that stopped being measured.
$previouslyAccepted = @()

if (Test-Path $BaselinePath) {
    $existing = Get-Content $BaselinePath -Raw | ConvertFrom-Json
    foreach ($property in $existing.classes.PSObject.Properties) {
        $previouslyAccepted += $property.Name
        if ($property.Value.PSObject.Properties.Name -contains 'reason') {
            $reasons[$property.Name] = [string] $property.Value.reason
        }
    }
    if ($existing.PSObject.Properties.Name -contains 'excluded' -and $existing.excluded) {
        foreach ($property in $existing.excluded.PSObject.Properties) {
            $excludedReasons[$property.Name] =
                if ($property.Value.PSObject.Properties.Name -contains 'reason') { [string] $property.Value.reason } else { '' }
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
    # A class that was in the ledger and is no longer measured has not been fixed - it has stopped
    # being measured, and rewriting `classes` from what the report contains would drop it silently.
    # Carry it into `excluded` with the same placeholder new debt gets, so the gate then refuses the
    # baseline until someone says why it is not measurable (B104).
    $exclusions = [ordered] @{}
    foreach ($key in (@($excludedReasons.Keys) + @($previouslyAccepted | Where-Object { -not $measured.Contains($_) }) | Sort-Object -Unique)) {
        $exclusions[$key] = [ordered] @{
            reason = if ($excludedReasons.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($excludedReasons[$key])) { $excludedReasons[$key] } else { $NeedsReason }
        }
    }

    $payload = [ordered] @{
        '_comment'   = 'Classes below the coverage bar, accepted as existing debt. Every entry carries a reason, and the build fails on one that does not - accepting debt is a decision, not a keystroke. The build also fails if a class goes further backwards, or if a class arrives below the bar and is not listed here. "excluded" is the separate list of classes the report does not measure at all: a baselined class that vanishes from the report is not a class that was fixed, and without that list it could leave the ledger by ceasing to be measured. Regenerate with build/check-coverage.ps1 -UpdateBaseline, then write a reason for anything new and review the diff.'
        minimumLines = $MinimumLines
        bars         = $bars
        classes      = $entries
        excluded     = $exclusions
    }
    $payload | ConvertTo-Json -Depth 5 | Set-Content $BaselinePath -Encoding utf8
    Write-Host "Recorded $($entries.Count) class(es) in $BaselinePath" -ForegroundColor Green

    $unexplained = @($entries.Keys | Where-Object { $entries[$_].reason -eq $NeedsReason }) +
                   @($exclusions.Keys | Where-Object { $exclusions[$_].reason -eq $NeedsReason })
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

# B104. "Recovered" used to mean any baselined class not currently below its bar - which included
# every class that had left the report entirely, reported in the same words as one that was
# genuinely fixed. So debt could be paid off by ceasing to be measured, and the run that found this
# said "MLQT.McpServer::Program now meets the bar" about a class the report no longer contained.
# Recovered now means measured and meeting its bar; not measured is its own outcome, and is only
# acceptable when the ledger says so and says why.
$recovered = $accepted.Keys | Where-Object { $measured.Contains($_) -and $_ -notin $below.Key }
$vanished  = $accepted.Keys | Where-Object { -not $measured.Contains($_) -and -not $excludedReasons.ContainsKey($_) }

# An exclusion that has started being measured again is stale: it is now under the gate like
# anything else, and leaving it listed would hide a real regression in it later.
$staleExclusions = $excludedReasons.Keys | Where-Object { $measured.Contains($_) }

$unexplainedExclusions = $excludedReasons.Keys | Where-Object {
    [string]::IsNullOrWhiteSpace($excludedReasons[$_]) -or $excludedReasons[$_] -eq $NeedsReason
}

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

if ($vanished) {
    Write-Host ''
    Write-Host 'Accepted debt that is no longer measured at all:' -ForegroundColor Red
    foreach ($key in ($vanished | Sort-Object)) { Write-Host "  $key" -ForegroundColor Red }
}

if ($staleExclusions) {
    Write-Host ''
    Write-Host 'Listed as not measurable, but the report measured them - drop them from "excluded":' -ForegroundColor Red
    foreach ($key in ($staleExclusions | Sort-Object)) { Write-Host "  $key" -ForegroundColor Red }
}

if ($unexplainedExclusions) {
    Write-Host ''
    Write-Host 'Excluded from measurement with no reason recorded:' -ForegroundColor Red
    foreach ($key in ($unexplainedExclusions | Sort-Object)) { Write-Host "  $key" -ForegroundColor Red }
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

if ($vanished) {
    Write-Host ''
    Fail "$(@($vanished).Count) baselined class(es) are not in the coverage report at all. That is not the same as meeting the bar - it is no information, and treating it as a pass lets debt leave the ledger by ceasing to be measured. Either find out why the class stopped being measured, or record it under `"excluded`" in $BaselinePath with a reason"
}

if ($staleExclusions) {
    Write-Host ''
    Fail "$(@($staleExclusions).Count) class(es) listed as not measurable are being measured. Remove them from `"excluded`" in $BaselinePath so the gate holds them to their bar like everything else"
}

if ($unexplainedExclusions) {
    Write-Host ''
    Fail "$(@($unexplainedExclusions).Count) exclusion(s) carry no reason. Write one in $BaselinePath saying why the class cannot be measured - the same rule the debt entries follow, for the same reason"
}

if ($unexplained.Count -gt 0) {
    Write-Host ''
    Fail "$($unexplained.Count) baseline entr(y/ies) carry no reason. Write one in $BaselinePath saying why the class is below its bar - 'needs a working SVN server' and 'nobody has written the tests yet' are both fine, and are different facts"
}

Write-Host ''
Write-Host 'Coverage gate passed.' -ForegroundColor Green
Pop-Location
exit 0
