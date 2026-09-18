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
    The source project to mutate. Defaults to MLQT.Services.

.PARAMETER TestProject
    The suite that must object. Defaults to the matching .Tests project.

.PARAMETER Mutate
    A file pattern limiting what is mutated, e.g. '**/ProjectNameRules.cs' or '**/Checking/*.cs'.
    STRONGLY RECOMMENDED. A whole assembly takes hours; one file takes about three minutes, most of
    which is the initial build and baseline test run.

.PARAMETER Output
    Where the report goes. Defaults to a temporary directory, because the reports are large and
    nothing in the repository should depend on them.

.EXAMPLE
    ./build/run-mutation.ps1 -Mutate '**/ProjectNameRules.cs'

.EXAMPLE
    ./build/run-mutation.ps1 -Project ModelicaParser -Mutate '**/Helpers/*.cs'

.NOTES
    Requires: dotnet tool install --global dotnet-stryker

    -t mtp is not optional and not discoverable. Every test project here is xUnit v3, which *is*
    Microsoft.Testing.Platform, and Stryker defaults to VSTest. Without the flag it fails with
    "not yet supported by Stryker, see issue 3094" and names every test project - which reads as
    "this repository cannot be mutation tested" and is wrong. The option is in --help and not in
    that message.
#>
[CmdletBinding()]
param(
    [string] $Project = 'MLQT.Services',
    [string] $TestProject = '',
    [string] $Mutate = '',
    [string] $Output = '',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    if (-not (Get-Command dotnet-stryker -ErrorAction SilentlyContinue)) {
        Write-Host "dotnet-stryker is not installed. Install it with:" -ForegroundColor Red
        Write-Host "  dotnet tool install --global dotnet-stryker" -ForegroundColor Yellow
        exit 1
    }

    if (-not $TestProject) { $TestProject = "$Project.Tests" }

    $projectFile = Join-Path $repoRoot "$Project/$Project.csproj"
    $testProjectFile = Join-Path $repoRoot "$TestProject/$TestProject.csproj"

    foreach ($file in @($projectFile, $testProjectFile)) {
        if (-not (Test-Path $file)) {
            Write-Host "FAIL: no such project: $file" -ForegroundColor Red
            exit 1
        }
    }

    if (-not $Output) {
        $Output = Join-Path ([System.IO.Path]::GetTempPath()) "mlqt-mutation-$(Get-Date -Format yyyyMMdd-HHmmss)"
    }

    if (-not $Mutate) {
        Write-Host "No -Mutate filter given, so the whole of $Project will be mutated." -ForegroundColor Yellow
        Write-Host "That takes hours. Ctrl-C now and pass -Mutate '**/SomeFile.cs' to scope it." -ForegroundColor Yellow
    }

    $arguments = @(
        '--project', "$Project.csproj"
        '--test-project', $testProjectFile
        '--test-runner', 'mtp'
        '--configuration', $Configuration
        '--reporter', 'json'
        '--reporter', 'cleartext'
        '--output', $Output
    )
    if ($Mutate) { $arguments += @('--mutate', $Mutate) }

    Write-Host "Mutating $Project$(if ($Mutate) { " ($Mutate)" }) against $TestProject" -ForegroundColor Cyan
    & dotnet-stryker @arguments
    $strykerExit = $LASTEXITCODE

    # The survivors are the output worth reading, and the cleartext table does not list them - it
    # gives a score per file. Pull them out of the JSON with the line each one changed.
    $report = Join-Path $Output 'reports/mutation-report.json'
    if (Test-Path $report) {
        $json = Get-Content $report -Raw | ConvertFrom-Json
        $survivors = @()
        $mutantCount = 0

        foreach ($fileName in $json.files.PSObject.Properties.Name) {
            $file = $json.files.$fileName
            $lines = $file.source -split "`n"
            foreach ($mutant in $file.mutants) {
                # Only mutants that were actually run count. 'Ignored' means the filter excluded it
                # and 'CompileError' means the change did not build - neither says anything about
                # the tests, and counting them is how a run that mutated nothing reports success.
                if ($mutant.status -in @('Killed', 'Survived', 'Timeout', 'NoCoverage')) {
                    $mutantCount++
                }
                if ($mutant.status -in @('Survived', 'NoCoverage')) {
                    $lineNumber = $mutant.location.start.line
                    $survivors += [pscustomobject]@{
                        File   = $fileName
                        Line   = $lineNumber
                        Status = $mutant.status
                        Change = $mutant.mutatorName
                        Source = ($lines[$lineNumber - 1]).Trim()
                    }
                }
            }
        }

        Write-Host ''
        if ($mutantCount -eq 0) {
            # Reporting "nothing survived" when nothing ran is how a tool tells you the code is well
            # tested while having tested nothing - the exact failure this script exists to find. It
            # happened here on the first try, and the first version of this guard missed it by
            # counting every mutant rather than only the ones that were executed.
            Write-Host "NO MUTANT WAS RUN. This is not a pass." -ForegroundColor Red
            Write-Host "  Every mutant was filtered out or failed to compile, so the suite was never" -ForegroundColor Red
            Write-Host "  asked anything. -Mutate '$Mutate' most likely matched no file in ${Project}:" -ForegroundColor Red
            Write-Host "  the pattern is relative to the project being mutated, so '**/Foo.cs' finds" -ForegroundColor Red
            Write-Host "  nothing when Foo.cs belongs to another assembly. Check -Project too." -ForegroundColor Red
            exit 1
        }

        if ($survivors.Count -eq 0) {
            Write-Host "No surviving mutants out of ${mutantCount}: every change the tool made broke a test." -ForegroundColor Green
        }
        else {
            Write-Host "$($survivors.Count) of $mutantCount mutant(s) survived - each is a statement the suite runs but does not check:" -ForegroundColor Yellow
            foreach ($survivor in $survivors) {
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
