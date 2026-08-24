<#
.SYNOPSIS
    Diffs an MLQT CLI check against the desktop app's exported issue list.

.DESCRIPTION
    When the app and CI disagree on how many issues a library has, the count on its own says
    nothing about why. This pairs the two exports up on model + rule + line and reports what each
    has that the other does not, grouped by rule and by library — because the difference almost
    always clusters, and the cluster names the cause.

    Both files use the same field names, so no translation is needed between them.

    Produce the two inputs with:
        mlqt check <library> --format json --out cli.json
        Code Review tab -> the download button in the issues toolbar

    Needs nothing installed: PowerShell reads JSON natively.

.PARAMETER CliJson
    The CLI's --format json output.

.PARAMETER GuiJson
    The app's exported issue list (mlqt-issues-<timestamp>.json).

.PARAMETER Detail
    Also list the individual differing issues, not just the counts per rule.

.EXAMPLE
    .\build\Compare-Findings.ps1 -CliJson .\cli.json -GuiJson .\mlqt-issues-20260824-141530.json

.EXAMPLE
    .\build\Compare-Findings.ps1 cli.json gui.json -Detail | Out-File diff.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)] [string] $CliJson,
    [Parameter(Mandatory, Position = 1)] [string] $GuiJson,
    [switch] $Detail
)

$ErrorActionPreference = 'Stop'

function Read-Findings([string] $path, [string] $label) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "$label file not found: $path"
    }

    $document = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
    if ($null -eq $document.findings) {
        throw "$label file has no 'findings' array: $path"
    }

    # The leading comma is load-bearing: PowerShell unrolls an array on return, so a file with a
    # single finding would come back as a bare object whose .Count is empty rather than 1.
    return ,@($document.findings)
}

# Model + rule + line, which is what identifies "the same issue" across the two tools. The
# fingerprint would be stricter, but it moves when a file is reformatted and would then report every
# issue as different for a reason that has nothing to do with the tools disagreeing.
function Get-Key($finding) { "$($finding.Model)|$($finding.RuleId)|$($finding.Line)" }

function New-KeySet($findings) {
    $set = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($finding in $findings) { [void]$set.Add((Get-Key $finding)) }
    return $set
}

$cli = Read-Findings $CliJson 'CLI'
$gui = Read-Findings $GuiJson 'App'

$cliKeys = New-KeySet $cli
$guiKeys = New-KeySet $gui

$onlyCli = @($cli | Where-Object { -not $guiKeys.Contains((Get-Key $_)) })
$onlyGui = @($gui | Where-Object { -not $cliKeys.Contains((Get-Key $_)) })

Write-Output ''
Write-Output ("CLI {0}    App {1}    difference {2}" -f $cli.Count, $gui.Count, ($cli.Count - $gui.Count))

function Write-Section($title, $findings) {
    Write-Output ''
    Write-Output "$title ($($findings.Count))"

    if ($findings.Count -eq 0) {
        Write-Output '    none'
        return
    }

    Write-Output '  by rule:'
    $findings | Group-Object RuleId | Sort-Object Count -Descending | ForEach-Object {
        '    {0,6}  {1,-38} e.g. {2}:{3}' -f $_.Count, $_.Name, $_.Group[0].Model, $_.Group[0].Line
    }

    Write-Output '  by library:'
    $findings | Group-Object { ($_.Model -split '\.')[0] } | Sort-Object Count -Descending | ForEach-Object {
        '    {0,6}  {1}' -f $_.Count, $_.Name
    }

    if ($Detail) {
        Write-Output '  issues:'
        $findings | Sort-Object Model, Line | ForEach-Object {
            '    {0}:{1}  {2}  {3}' -f $_.Model, $_.Line, $_.RuleId, $_.Message
        }
    }
}

Write-Section 'Only the CLI reports' $onlyCli
Write-Section 'Only the app reports' $onlyGui

# The single most useful discriminator. If the exclusive findings sit on models the other side says
# nothing about at all, that side never checked those classes — a loading, queueing or scoping
# problem. If they sit on models both sides report on, both checked the class and disagreed about
# it — a settings or rule problem. The two have completely different causes.
function Write-ModelSplit($title, $findings, $otherFindings) {
    if ($findings.Count -eq 0) { return }

    $otherModels = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($finding in $otherFindings) { [void]$otherModels.Add($finding.Model) }

    $unseen = @($findings | Where-Object { -not $otherModels.Contains($_.Model) })
    $shared = @($findings | Where-Object { $otherModels.Contains($_.Model) })

    $unseenModels = @($unseen | Group-Object Model).Count
    $sharedModels = @($shared | Group-Object Model).Count

    Write-Output ''
    Write-Output "$title"
    Write-Output ("    {0,6} on {1} model(s) the other side reports nothing for  -> not checked there" -f $unseen.Count, $unseenModels)
    Write-Output ("    {0,6} on {1} model(s) both sides report on                -> checked, but disagreed" -f $shared.Count, $sharedModels)

    if ($unseenModels -gt 0) {
        Write-Output '    models only one side reports on (first 15):'
        $unseen | Group-Object Model | Select-Object -First 15 | ForEach-Object { '      ' + $_.Name }
    }
}

Write-ModelSplit 'CLI-only findings, split by whether the app reports on that model:' $onlyCli $gui
Write-ModelSplit 'App-only findings, split by whether the CLI reports on that model:' $onlyGui $cli

if ($onlyCli.Count -eq 0 -and $onlyGui.Count -eq 0) {
    Write-Output ''
    Write-Output 'The two agree exactly.'
}
else {
    Write-Output ''
    Write-Output 'Where a cluster points:'
    Write-Output '  one rule id            - that rule is enabled on one side and not the other'
    Write-Output '  one library prefix     - that library is loaded by one side and not the other,'
    Write-Output '                           or is excluded by ExcludedLibraries in only one of them'
    Write-Output '  MLQT.Parse.*           - parse diagnostics; the CLI emits these for its checked'
    Write-Output '                           set regardless of which rules are enabled'
    Write-Output '  spread evenly          - a filter left on in the app (search box, "Only this'
    Write-Output '                           model", or "Changes vs baseline"); note the export'
    Write-Output '                           ignores those, so re-export rather than re-filter'
}
