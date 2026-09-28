<#
.SYNOPSIS
    Which assemblies are ours, which of them are gated, and how a merged report is produced.

.DESCRIPTION
    Dot-sourced by both build/check-coverage.ps1 and build/run-all-tests.ps1. It exists so the two
    scripts cannot disagree about what "our code" means - a disagreement that has already cost this
    repository once: the report used a *deny* list naming two assemblies and measured everything
    else, so SharpCompress, LibGit2Sharp, Moq, FluentValidation and bunit made up 79% of the
    denominator and the headline read 19.2% against a real 69.4% (backlog B117).

    Two lists, because they answer different questions:

      $MlqtBars              the assemblies the gate enforces a per-class bar on. Every one has a
                             suite that runs in CI, so a number here is a number CI can defend.

      $MlqtOwnedAssemblies   everything we wrote. Today the same list: DymolaInterface and
                             OpenModelicaInterface were once only here, because their suites drive a
                             live simulation tool and ran in no CI job. Since B399 CI runs the classes
                             of those suites needing no tool, and since B438 the gate measures the two
                             from that part, filtered the same way - so what only a live tool reaches
                             is debt in the ledger with that reason, not an assembly left unmeasured.

    Kept as two names because they are still two questions: an assembly of ours with no suite CI
    can run would belong in the second and not the first.
#>

# The gate's per-class bars. ModelicaParser is higher because CLAUDE.md singles it out as critical.
$MlqtBars = @{
    'ModelicaParser'  = 95.0
    'ModelicaGraph'   = 80.0
    'MLQT.Services'   = 80.0
    'MLQT.McpServer'  = 80.0
    'RevisionControl' = 80.0
    'mlqt'            = 80.0   # the assembly name of MLQT.Cli, from its ToolCommandName
    'MLQT.Shared'     = 80.0   # joined the gate in phase 7a-5
    # Measured from their tests needing no tool - the trait filters CI uses (B399, B438).
    'DymolaInterface'       = 80.0
    'OpenModelicaInterface' = 80.0
}

# Everything we own. Every assembly of ours is gated at present (B438).
$MlqtOwnedAssemblies = @($MlqtBars.Keys)

<#
.SYNOPSIS
    Merges the cobertura reports under a results directory into a single summary.

.PARAMETER Assemblies
    The allow list. Anything not named is excluded, which is the whole point - see B117.
#>
<#
.SYNOPSIS
    The suites whose results directory holds more than one coverage report.
.DESCRIPTION
    Backlog B123. coverlet names its output coverage.cobertura.<timestamp>.xml, so a results
    directory that is not cleared accumulates a file per run rather than overwriting - and
    reportgenerator unions line *numbers*, so merging a report taken before a file changed with one
    taken after invents coverable lines that no longer exist and that nothing ever hit. A class at
    96% then reads as 56%, with a coverable count matching no version of the file, and it looks like
    a defect in the code rather than in the measurement. It was believed as debt for three days.

    Called by check-coverage.ps1, which is the script that can meet this: it has -SkipTests, which
    reuses whatever is on disk. run-all-tests.ps1 deletes the directory whenever it collects, so the
    situation cannot arise there, and a guard that cannot fire is worse than none - it reads as
    protection. It lives here rather than in the gate because it is a fact about merging cobertura
    files, which is what this file is for, and because the day run-all-tests.ps1 stops clearing is
    the day it needs this too.
#>
function Get-DuplicateMlqtCoverageReports {
    param([Parameter(Mandatory)] [string] $ResultsDirectory)

    Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura*.xml' -ErrorAction SilentlyContinue |
        Group-Object { $_.Directory.Name } |
        Where-Object { $_.Count -gt 1 }
}

function New-MlqtCoverageReport {
    param(
        [Parameter(Mandatory)] [string]   $ResultsDirectory,
        [Parameter(Mandatory)] [string]   $ReportDirectory,
        [Parameter(Mandatory)] [string[]] $Assemblies
    )

    $assemblyFilters = '-assemblyfilters:' + (($Assemblies | Sort-Object | ForEach-Object { "+$_" }) -join ';')

    # The class filters drop generated code: ANTLR's lexer and parser, which are enormous and
    # machine-written, and the regex source generator's output. coverlet.MTP already omits most
    # generated code; these are the ones it does not recognise as such.
    & reportgenerator `
        "-reports:$ResultsDirectory/**/coverage.cobertura*.xml" `
        "-targetdir:$ReportDirectory" `
        '-reporttypes:JsonSummary;TextSummary;HtmlSummary' `
        $assemblyFilters `
        '-classfilters:-System.Text.RegularExpressions.Generated*;-modelicaParser;-modelicaLexer;-modelicaBaseListener;-modelicaBaseVisitor*' | Out-Null

    return $LASTEXITCODE -eq 0
}
