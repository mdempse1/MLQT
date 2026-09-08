<#
.SYNOPSIS
    Runs every test suite in the solution, including the two CI cannot run.

.DESCRIPTION
    There was no way to run all the tests locally. `check-coverage.ps1` runs seven of the nine suites
    - it exists to gate coverage, and the two it omits contribute none - and CI runs the same seven
    plus the journeys. So `DymolaInterface.Tests` and `OpenModelicaInterface.Tests` were run only by
    whoever remembered they existed, which for a while was nobody: 207 Dymola tests had never run in
    any automated context.

    CI is right to skip them. They drive a live Dymola or OpenModelica install that no runner has,
    and the workflow says so. But a developer machine often *does* have one, and on a machine that
    does, they are the only tests that cover those interfaces at all.

    The suite list is read from MLQT.slnx rather than written out here. This repository has been
    caught repeatedly by one rule with two implementations - a test project added to the solution and
    not to a list is exactly that shape - so there is no list to forget to update.

.PARAMETER Configuration
    Build configuration. Release by default, to match CI and the coverage gate.

.PARAMETER CoreOnly
    Skip the suites that need an external simulation tool, leaving what CI runs. Use this to ask
    "would CI be green?" without the noise of tools the runner would not have either.

.PARAMETER SkipBuild
    Run the suites as they were last built. Much faster when iterating on one of them.

.NOTES
    A failure is a failure, whichever suite it is in. An earlier version excused failures in the
    tool-dependent suites on the grounds that the machine might not have the tool - and then quietly
    excused a real one: OpenModelica *is* installed here, and
    GetErrorStringAsync_AfterClear_ReturnsEmpty fails against it. Excusing by category hides the
    thing you wanted to find. A machine without the tools uses -CoreOnly, which is a decision rather
    than a shrug.

.EXAMPLE
    ./build/run-all-tests.ps1
    Everything, including the simulation interfaces.

.EXAMPLE
    ./build/run-all-tests.ps1 -CoreOnly -SkipBuild
    The seven suites CI runs, against the current build. Use -CoreOnly on a machine without Dymola
    or OpenModelica, rather than reading past their failures.
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $CoreOnly,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot

# Suites needing something the machine may not have, and the filter that makes the rest of the suite
# runnable anyway. Anything not named here runs unfiltered.
#
# The SVN exclusion is the same one build-and-test.yml and check-coverage.ps1 apply, and for the same
# reason: those tests want a working copy at C:\Projects\ModelicaEditorTest and a server. Kept
# identical so a local run and a CI run measure the same thing.
$suiteNotes = @{
    'RevisionControl.Tests' = @{
        Filter       = 'FullyQualifiedName!~Svn'
        Why          = 'SVN integration tests need a working copy and a server'
        NeedsTooling = $false
    }
    'DymolaInterface.Tests' = @{
        Filter       = $null
        Why          = 'drives a live Dymola install'
        NeedsTooling = $true
    }
    'OpenModelicaInterface.Tests' = @{
        Filter       = $null
        Why          = 'drives a live OpenModelica (omc) install'
        NeedsTooling = $true
    }
    'MLQT.Journeys' = @{
        Filter       = $null
        Why          = 'drives a real browser; needs playwright.ps1 install chromium'
        NeedsTooling = $true
    }
}

function Fail([string] $message) {
    Write-Host "FAIL: $message" -ForegroundColor Red
    Pop-Location
    exit 1
}

# --- the suites, from the solution ------------------------------------------------------------

$solution = Join-Path $repositoryRoot 'MLQT.slnx'
if (-not (Test-Path $solution)) { Fail "MLQT.slnx not found at $solution" }

$projects = ([xml](Get-Content $solution)).Solution.Project.Path |
    Where-Object { $_ -match '\.Tests\.csproj$' -or $_ -match 'MLQT\.Journeys\.csproj$' } |
    Sort-Object

if ($projects.Count -lt 8) {
    Fail "only found $($projects.Count) test projects in MLQT.slnx; the solution format may have changed"
}

$suites = foreach ($path in $projects) {
    $name = [IO.Path]::GetFileNameWithoutExtension($path)
    $note = $suiteNotes[$name]

    [pscustomobject]@{
        Name         = $name
        Project      = $path
        Filter       = if ($note) { $note.Filter } else { $null }
        Why          = if ($note) { $note.Why } else { $null }
        NeedsTooling = if ($note) { [bool]$note.NeedsTooling } else { $false }
    }
}

if ($CoreOnly) {
    $skipped = $suites | Where-Object NeedsTooling
    $suites = $suites | Where-Object { -not $_.NeedsTooling }
    foreach ($s in $skipped) { Write-Host "Skipping $($s.Name) - $($s.Why)" -ForegroundColor DarkGray }
}

Write-Host "Running $($suites.Count) suite(s) in $Configuration" -ForegroundColor Cyan
Write-Host ''

# --- build ------------------------------------------------------------------------------------

if (-not $SkipBuild) {
    Write-Host 'Building the solution...' -ForegroundColor Cyan
    dotnet build $solution -c $Configuration --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Fail 'the solution did not build. Note the MAUI projects cannot build while MLQT.exe is running.'
    }
}

# --- run --------------------------------------------------------------------------------------

$results = foreach ($suite in $suites) {
    Write-Host "  $($suite.Name)" -NoNewline

    $arguments = @('test', $suite.Project, '-c', $Configuration, '--no-build')
    if ($suite.Filter) { $arguments += @('--filter', $suite.Filter) }

    $started = Get-Date
    $output = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $elapsed = (Get-Date) - $started

    # The Microsoft.Testing.Platform summary. VSTest's "--logger" is not valid here and presents as
    # "0 tests ran" rather than an error, which is why nothing in this repository uses it any more.
    $text = $output -join "`n"
    $total  = if ($text -match '(?m)^\s*total:\s*(\d+)')     { [int]$Matches[1] } else { $null }
    $failed = if ($text -match '(?m)^\s*failed:\s*(\d+)')    { [int]$Matches[1] } else { $null }

    if ($exitCode -eq 0) {
        Write-Host "  $total passed  ($([int]$elapsed.TotalSeconds)s)" -ForegroundColor Green
    }
    else {
        $colour = 'Red'
        $count  = if ($null -ne $failed) { "$failed failed" } else { 'did not run' }
        Write-Host "  $count of $total  ($([int]$elapsed.TotalSeconds)s)" -ForegroundColor $colour
        if ($suite.Why) { Write-Host "      $($suite.Why)" -ForegroundColor DarkGray }

        # Only the failing assertions, not the whole run.
        $output | Select-String '^\s*failed ' | Select-Object -First 10 | ForEach-Object {
            Write-Host "      $($_.Line.Trim())" -ForegroundColor DarkGray
        }
    }

    [pscustomobject]@{
        Name         = $suite.Name
        Total        = $total
        Failed       = $failed
        ExitCode     = $exitCode
        NeedsTooling = $suite.NeedsTooling
        Seconds      = [int]$elapsed.TotalSeconds
    }
}

# --- verdict ----------------------------------------------------------------------------------

Write-Host ''
$totalTests = ($results | Measure-Object -Property Total -Sum).Sum
$totalTime  = ($results | Measure-Object -Property Seconds -Sum).Sum
Write-Host "$totalTests test(s) across $($results.Count) suite(s) in $totalTime s" -ForegroundColor Cyan

$failures = $results | Where-Object { $_.ExitCode -ne 0 }

foreach ($f in $failures | Where-Object NeedsTooling) {
    Write-Host "  $($f.Name) needs an external tool - if this machine has none, use -CoreOnly" -ForegroundColor DarkGray
}

if ($failures) {
    Fail "these suites failed: $(($failures | ForEach-Object Name) -join ', ')"
}

# Not "all suites passed" when one did not. A summary that contradicts the lines above it is how a
# green run stops meaning anything, which is the failure this repository keeps finding in its own
# checks rather than in its code.
Write-Host 'All suites passed.' -ForegroundColor Green

Pop-Location
exit 0
