<#
.SYNOPSIS
    Mutation testing: does the suite notice when the code is wrong?

.DESCRIPTION
    Coverage says a line ran. Mutation testing says a line was *checked*: it changes the code in
    small ways - an operator flipped, a condition inverted, a string emptied - and reports the
    changes no test objected to. A surviving mutant is a statement the suite executes and does not
    depend on.

    This exists because phase 1 produced six tests that asserted something they could not see, and
    every one was found by accident - a mutation check somebody happened to run by hand, a positive
    control, a coverage figure that moved between runs. A source-scanning guard was tried and
    abandoned: the shape needs semantics, not syntax, and the rule that looks obvious ("every
    assertion is behind an if") would not have caught the test that started it. This is the
    mechanical answer, and it caught two real gaps the first time it was pointed at a file - an
    entire public method with no test, and a user-facing message that could be emptied unnoticed
    (backlog B212).

    IT IS A REPORT, NOT A GATE. It is far too slow for a push, and a mutation score is not a number
    to chase: some survivors are equivalent mutants that cannot be killed, and some are in code
    nobody should write a test for. Read the survivors, not the percentage.

.PARAMETER Project
    The source project to mutate. Defaults to MLQT.Services. Ignored with -All.

.PARAMETER TestProject
    The suite that must object. Defaults to the matching .Tests project.

.PARAMETER Mutate
    One or more file patterns limiting what is mutated, e.g. '**/ProjectNameRules.cs' or
    '**/Checking/*.cs'. STRONGLY RECOMMENDED for a single run. A whole assembly takes hours; one file
    takes about three minutes, almost all of it the initial build and baseline test run - which is
    why several files are worth passing in one call rather than one run each.

.PARAMETER All
    Mutate every measured assembly in turn and write one report over the lot. Takes many hours and is
    resumable: a project whose report is already present is skipped.

.PARAMETER Summarise
    Rebuild the consolidated report from runs already on disk, mutating nothing.

.PARAMETER Force
    With -All, re-run a project whose report is already there.

.PARAMETER Output
    Where reports go. Defaults to a temporary directory for a single run, and to MutationReport/ for
    -All, which is git-ignored and stable so a campaign can be resumed into it.

.PARAMETER Concurrency
    Test runners to use at once. Stryker's own default is sensible; raise it on a machine with cores
    to spare, lower it if the run makes the machine unusable.

.EXAMPLE
    ./build/run-mutation.ps1 -Mutate '**/ProjectNameRules.cs'

.EXAMPLE
    ./build/run-mutation.ps1 -Project ModelicaParser -Mutate '**/Helpers/*.cs'

.EXAMPLE
    ./build/run-mutation.ps1 -All

.EXAMPLE
    ./build/run-mutation.ps1 -Summarise

.NOTES
    Requires: dotnet tool install --global dotnet-stryker

    --test-runner mtp is not optional and not discoverable. Every test project here is xUnit v3,
    which *is* Microsoft.Testing.Platform, and Stryker defaults to VSTest. Without the flag it fails
    with "not yet supported by Stryker, see issue 3094" and names every test project - which reads as
    "this repository cannot be mutation tested" and is wrong. The option is in --help and not in that
    message.
#>
[CmdletBinding()]
param(
    [string] $Project = 'MLQT.Services',
    [string] $TestProject = '',
    [string[]] $Mutate = @(),
    [string] $Output = '',
    [string] $Configuration = 'Release',
    [switch] $All,
    [switch] $Summarise,
    [switch] $Force,
    [int] $Concurrency = 0
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

# The seven assemblies the coverage gate measures, so the two agree about what "our code" means.
# Ordered smallest-first on purpose: the early ones calibrate how long this machine takes before
# anything committing starts, and a campaign abandoned half way still leaves useful reports.
# 'mlqt' is the assembly name of MLQT.Cli, which is why this maps project to test project by name
# rather than reusing CoverageAssemblies.ps1's assembly-keyed list.
$MutationProjects = @(
    @{ Project = 'MLQT.Cli';        TestProject = 'MLQT.Cli.Tests' }
    @{ Project = 'MLQT.McpServer';  TestProject = 'MLQT.McpServer.Tests' }
    @{ Project = 'RevisionControl'; TestProject = 'RevisionControl.Tests' }
    @{ Project = 'MLQT.Services';   TestProject = 'MLQT.Services.Tests' }
    @{ Project = 'ModelicaGraph';   TestProject = 'ModelicaGraph.Tests' }
    @{ Project = 'MLQT.Shared';     TestProject = 'MLQT.Shared.Tests' }
    @{ Project = 'ModelicaParser';  TestProject = 'ModelicaParser.Tests' }
)

function Get-ReportPath([string] $directory) {
    Join-Path $directory 'reports/mutation-report.json'
}

# The mutants a report says were never checked, with the line each one changed.
#
# Only mutants that were actually run are counted. 'Ignored' means the filter excluded it and
# 'CompileError' means the change did not build - neither says anything about the tests, and counting
# them is how a run that mutated nothing reports success.
function Read-MutationReport([string] $reportPath, [string] $project) {
    $json = Get-Content $reportPath -Raw | ConvertFrom-Json
    # A List, not an array: a whole assembly is tens of thousands of mutants and `+=` on an array
    # copies it every time, which turns reading the report into the slow part of the campaign.
    $survivors = [System.Collections.Generic.List[object]]::new()
    $executed = 0

    foreach ($fileName in $json.files.PSObject.Properties.Name) {
        $file = $json.files.$fileName
        $lines = $file.source -split "`n"
        foreach ($mutant in $file.mutants) {
            if ($mutant.status -in @('Killed', 'Survived', 'Timeout', 'NoCoverage')) { $executed++ }
            if ($mutant.status -in @('Survived', 'NoCoverage')) {
                $lineNumber = $mutant.location.start.line
                $survivors.Add([pscustomobject]@{
                    Project = $project
                    File    = $fileName
                    Line    = $lineNumber
                    Status  = $mutant.status
                    Change  = $mutant.mutatorName
                    Source  = "$($lines[$lineNumber - 1])".Trim()
                })
            }
        }
    }

    return [pscustomobject]@{ Executed = $executed; Survivors = $survivors }
}

# One markdown file listing every surviving mutant across every project that has been run.
function Write-ConsolidatedReport([string] $root) {
    $all = @()
    $ran = @()

    foreach ($entry in $MutationProjects) {
        $reportPath = Get-ReportPath (Join-Path $root $entry.Project)
        if (-not (Test-Path $reportPath)) { continue }

        $result = Read-MutationReport $reportPath $entry.Project
        $score = if ($result.Executed -gt 0) {
            [math]::Round(100.0 * ($result.Executed - $result.Survivors.Count) / $result.Executed, 1)
        } else { 0 }

        $ran += [pscustomobject]@{
            Project  = $entry.Project
            Executed = $result.Executed
            Survived = $result.Survivors.Count
            Score    = $score
        }
        $all += $result.Survivors
    }

    if ($ran.Count -eq 0) {
        Write-Host "No mutation reports under $root yet." -ForegroundColor Yellow
        return
    }

    $summaryPath = Join-Path $root 'mutation-survivors.md'
    $out = [System.Collections.Generic.List[string]]::new()
    $out.Add('# Surviving mutants')
    $out.Add('')
    $out.Add("Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm'). A surviving mutant is a change to the code")
    $out.Add('that no test objected to - a statement the suite runs but does not depend on.')
    $out.Add('')
    $out.Add('**Not every survivor is a defect.** An equivalent mutant cannot be killed by any test, and')
    $out.Add('some code is not worth a test - an entry in a literal list of C header names, say. Read them;')
    $out.Add('do not chase the score.')
    $out.Add('')
    $out.Add('| Project | Mutants run | Survived | Score |')
    $out.Add('|---|---|---|---|')
    foreach ($row in $ran) {
        $out.Add("| $($row.Project) | $($row.Executed) | $($row.Survived) | $($row.Score)% |")
    }
    $out.Add('')

    foreach ($group in ($all | Group-Object Project | Sort-Object Name)) {
        $out.Add("## $($group.Name)")
        $out.Add('')
        foreach ($fileGroup in ($group.Group | Group-Object File | Sort-Object Name)) {
            $relative = $fileGroup.Name -replace [regex]::Escape($repoRoot + [IO.Path]::DirectorySeparatorChar), ''
            $out.Add("### $relative")
            $out.Add('')
            foreach ($survivor in ($fileGroup.Group | Sort-Object Line)) {
                $out.Add("- **line $($survivor.Line)** [$($survivor.Status)] $($survivor.Change)")
                $out.Add('  ```csharp')
                $out.Add("  $($survivor.Source)")
                $out.Add('  ```')
            }
            $out.Add('')
        }
    }

    Set-Content -Path $summaryPath -Value $out -Encoding utf8

    Write-Host ''
    Write-Host 'Mutation summary' -ForegroundColor Cyan
    $ran | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "$($all.Count) surviving mutant(s) across $($ran.Count) project(s)." -ForegroundColor Yellow
    Write-Host "Written to: $summaryPath" -ForegroundColor Cyan
}

# Mutates one project. Returns Stryker's exit code.
function Invoke-Stryker([string] $project, [string] $testProject, [string[]] $mutate, [string] $outputDirectory) {
    $testProjectFile = Join-Path $repoRoot "$testProject/$testProject.csproj"

    $arguments = @(
        '--project', "$project.csproj"
        '--test-project', $testProjectFile
        '--test-runner', 'mtp'
        '--configuration', $Configuration
        '--reporter', 'json'
        '--reporter', 'cleartext'
        '--output', $outputDirectory
    )
    foreach ($pattern in $mutate) { $arguments += @('--mutate', $pattern) }
    if ($Concurrency -gt 0) { $arguments += @('--concurrency', "$Concurrency") }

    # Out-Host, not the pipeline: Stryker's progress is the only sign of life during a run that can
    # last an hour, so it has to reach the console - but it must not be mixed into what this function
    # returns, which is the exit code alone.
    & dotnet-stryker @arguments | Out-Host
    return $LASTEXITCODE
}

try {
    if (-not (Get-Command dotnet-stryker -ErrorAction SilentlyContinue)) {
        Write-Host "dotnet-stryker is not installed. Install it with:" -ForegroundColor Red
        Write-Host "  dotnet tool install --global dotnet-stryker" -ForegroundColor Yellow
        exit 1
    }

    # A campaign wants a stable directory it can be resumed into, rather than a timestamp.
    if (-not $Output) {
        $Output = if ($All -or $Summarise) {
            Join-Path $repoRoot 'MutationReport'
        } else {
            Join-Path ([System.IO.Path]::GetTempPath()) "mlqt-mutation-$(Get-Date -Format yyyyMMdd-HHmmss)"
        }
    }

    if ($Summarise) {
        Write-ConsolidatedReport $Output
        exit 0
    }

    if ($All) {
        Write-Host "Mutating every measured assembly into $Output" -ForegroundColor Cyan
        Write-Host "This takes many hours. It is resumable: a project whose report is already there is" -ForegroundColor DarkGray
        Write-Host "skipped, so stopping with Ctrl-C and running again continues where it left off." -ForegroundColor DarkGray
        Write-Host ''

        $campaignStart = Get-Date
        foreach ($entry in $MutationProjects) {
            $projectDirectory = Join-Path $Output $entry.Project
            $reportPath = Get-ReportPath $projectDirectory

            if ((Test-Path $reportPath) -and -not $Force) {
                Write-Host "$($entry.Project): already done, skipping (use -Force to redo)" -ForegroundColor DarkGray
                continue
            }

            Write-Host "=== $($entry.Project) ===" -ForegroundColor Cyan
            $started = Get-Date
            [void](Invoke-Stryker $entry.Project $entry.TestProject @() $projectDirectory)
            $elapsed = (Get-Date) - $started

            if (Test-Path $reportPath) {
                $result = Read-MutationReport $reportPath $entry.Project
                Write-Host ("$($entry.Project): {0} mutants run, {1} survived, took {2:hh\:mm\:ss}" -f `
                    $result.Executed, $result.Survivors.Count, $elapsed) -ForegroundColor Green
            }
            else {
                # Not fatal for the campaign - the other projects are still worth having - but it has
                # to be said, because a missing report is silently indistinguishable from a clean one.
                Write-Host "$($entry.Project): NO REPORT PRODUCED. Stryker failed; see its output above." -ForegroundColor Red
            }
            Write-Host ''
        }

        Write-Host ("Campaign elapsed: {0:hh\:mm\:ss}" -f ((Get-Date) - $campaignStart)) -ForegroundColor Cyan
        Write-ConsolidatedReport $Output
        exit 0
    }

    # --- a single scoped run ---
    if (-not $TestProject) { $TestProject = "$Project.Tests" }

    $projectFile = Join-Path $repoRoot "$Project/$Project.csproj"
    $testProjectFile = Join-Path $repoRoot "$TestProject/$TestProject.csproj"

    foreach ($file in @($projectFile, $testProjectFile)) {
        if (-not (Test-Path $file)) {
            Write-Host "FAIL: no such project: $file" -ForegroundColor Red
            exit 1
        }
    }

    if ($Mutate.Count -eq 0) {
        Write-Host "No -Mutate filter given, so the whole of $Project will be mutated." -ForegroundColor Yellow
        Write-Host "That takes hours. Ctrl-C now and pass -Mutate '**/SomeFile.cs' to scope it," -ForegroundColor Yellow
        Write-Host "or -All to mutate everything deliberately." -ForegroundColor Yellow
    }

    Write-Host "Mutating $Project$(if ($Mutate.Count) { " ($($Mutate -join ', '))" }) against $TestProject" -ForegroundColor Cyan
    $strykerExit = Invoke-Stryker $Project $TestProject $Mutate $Output

    $report = Get-ReportPath $Output
    if (Test-Path $report) {
        $result = Read-MutationReport $report $Project

        Write-Host ''
        if ($result.Executed -eq 0) {
            # Reporting "nothing survived" when nothing ran is how a tool tells you the code is well
            # tested while having tested nothing - the exact failure this script exists to find. It
            # happened here on the first try, and the first version of this guard missed it by
            # counting every mutant rather than only the ones that were executed.
            Write-Host "NO MUTANT WAS RUN. This is not a pass." -ForegroundColor Red
            Write-Host "  Every mutant was filtered out or failed to compile, so the suite was never" -ForegroundColor Red
            Write-Host "  asked anything. -Mutate '$($Mutate -join "', '")' most likely matched no file" -ForegroundColor Red
            Write-Host "  in ${Project}:" -ForegroundColor Red
            Write-Host "  the pattern is relative to the project being mutated, so '**/Foo.cs' finds" -ForegroundColor Red
            Write-Host "  nothing when Foo.cs belongs to another assembly. Check -Project too." -ForegroundColor Red
            exit 1
        }

        if ($result.Survivors.Count -eq 0) {
            Write-Host "No surviving mutants out of $($result.Executed): every change the tool made broke a test." -ForegroundColor Green
        }
        else {
            Write-Host "$($result.Survivors.Count) of $($result.Executed) mutant(s) survived - each is a statement the suite runs but does not check:" -ForegroundColor Yellow
            foreach ($survivor in $result.Survivors) {
                Write-Host ("  {0}:{1}  [{2}] {3}" -f $survivor.File, $survivor.Line, $survivor.Status, $survivor.Change) -ForegroundColor Yellow
                Write-Host ("      {0}" -f $survivor.Source) -ForegroundColor DarkGray
            }
            Write-Host ''
            Write-Host "Not every survivor is a defect: an equivalent mutant cannot be killed by any test." -ForegroundColor DarkGray
            Write-Host "Read them; do not chase the score." -ForegroundColor DarkGray
        }

        Write-Host ''
        Write-Host "Full report: $report"
    }

    exit $strykerExit
}
finally {
    Pop-Location
}
