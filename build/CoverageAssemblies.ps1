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

      $MlqtOwnedAssemblies   everything we wrote, including DymolaInterface and OpenModelicaInterface,
                             whose suites drive a live simulation tool and therefore run in no CI job.
                             They are measurable locally by run-all-tests.ps1 and nowhere else.

    check-coverage.ps1 reports on the first list: including the other two would show 0% for code that
    is tested, just not by the suites it ran, which is worse than not showing them. run-all-tests.ps1
    reports on the second, because it is the one thing that runs every suite we have.
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
}

# Everything we own. The two additions are the simulation-tool interfaces: real code of ours, with
# real tests, that no runner can execute.
$MlqtOwnedAssemblies = @($MlqtBars.Keys) + @('DymolaInterface', 'OpenModelicaInterface')

<#
.SYNOPSIS
    Merges the cobertura reports under a results directory into a single summary.

.PARAMETER Assemblies
    The allow list. Anything not named is excluded, which is the whole point - see B117.
#>
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
