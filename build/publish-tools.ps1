<#
.SYNOPSIS
    Publishes MLQT's three shipping tools into one directory, then proves each of them runs.

.DESCRIPTION
    Phase 7b-7. One installer per platform carries the GUI, the `mlqt` CLI and the MCP server, so all
    three are published into a single tree: they are built from one repository, share every assembly
    below MLQT.Shared, and each carries its own `.deps.json` and `.runtimeconfig.json`. Publishing
    them together costs one copy of the shared assemblies rather than three.

    **The smoke tests are the point of this script.** Building an installer around a tree nobody has
    run is how you ship an application that does not start, and this phase has already found two of
    those by running things: the Photino host that resolved no web assets outside a publish, and the
    one that shipped no svn client. So each tool is asked a question only a working build can answer:

      mlqt          prints its version and exits 0
      MCP server    answers an MCP `initialize` handshake over stdio with its own name and version
      GUI           runs the 16 `/selftest` probes against the published tree and passes every one

    The last is the strongest thing available: it resolves the RCL assets, runs JavaScript interop,
    renders MudBlazor, and exercises settings, logging, the file picker and the svn locator — in the
    published layout, which is the layout that ships.

.PARAMETER Runtime
    The RID to publish for. `win-x64` builds what the Inno installer packages; `linux-x64` builds what
    the .deb packages.

.PARAMETER SelfContained
    Bundle the .NET runtime. The .deb does (Ubuntu and Debian carry no .NET 10, so a dependency on it
    would mean asking every user to add Microsoft's apt feed); the Windows installer does not, and
    downloads the runtime when it is absent.

.PARAMETER AllowMissingSvn
    Do not fail when the bundled svn client is absent. The payload is fetched by
    build/fetch-svn-tools.ps1 and is not committed, so a developer's tree does not have it — but a
    release build's must, and defaulting to failure is what stops a release shipping without it
    (backlog B144, where exactly that happened and nothing noticed for a whole phase).

.EXAMPLE
    pwsh build/publish-tools.ps1 -Version 1.2.3 -Output publish/win-x64 -AllowMissingSvn

.EXAMPLE
    pwsh build/publish-tools.ps1 -Runtime linux-x64 -SelfContained -Version 1.2.3 -Output publish/linux-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'win-arm64', 'linux-arm64')]
    [string] $Runtime = 'win-x64',

    [string] $Version = '0.0.0-dev',

    [Parameter(Mandatory)]
    [string] $Output,

    [switch] $SelfContained,
    [switch] $AllowMissingSvn,
    [switch] $SkipSmokeTests,

    # How long the GUI gets to run its probes and exit. Bounded because the failure it guards is a
    # host that starts and never renders: it does not crash, it waits, and an unbounded wait hands a
    # CI job its whole timeout - six hours of runner, reporting nothing about why (backlog B146).
    # Generous, because a cold first run on a slow runner does real work: WebView2 or WebKitGTK
    # starts, the RCL assets resolve and sixteen probes run.
    [int] $SelfTestTimeoutSeconds = 300,

    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$isWindowsTarget = $Runtime.StartsWith('win-')
$exe = if ($isWindowsTarget) { '.exe' } else { '' }

$tools = @(
    @{ Project = 'MLQT.Photino/MLQT.Photino.csproj';   File = "MLQT.Photino$exe";   Name = 'GUI' }
    @{ Project = 'MLQT.Cli/MLQT.Cli.csproj';           File = "mlqt$exe";           Name = 'CLI' }
    @{ Project = 'MLQT.McpServer/MLQT.McpServer.csproj'; File = "MLQT.McpServer$exe"; Name = 'MCP server' }
)

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path

Write-Host "Publishing 3 tools for $Runtime at $Version" -ForegroundColor Cyan

foreach ($tool in $tools) {
    Write-Host "  $($tool.Name)"
    dotnet publish (Join-Path $repo $tool.Project) `
        -c $Configuration `
        -r $Runtime `
        --self-contained $($SelfContained.IsPresent.ToString().ToLower()) `
        -p:Version=$Version `
        -o $Output `
        --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "publishing $($tool.Name) failed" }
}

# ---- the tree is what it claims to be -------------------------------------------------------------

$problems = @()

foreach ($tool in $tools) {
    if (-not (Test-Path (Join-Path $Output $tool.File))) {
        $problems += "$($tool.Name) is missing: $($tool.File)"
    }
}

# The host serves index.html and the RCL assets from here. Without it the window opens and loads
# nothing at all, with no error - which is what a built-but-not-published tree does (B133).
if (-not (Test-Path (Join-Path $Output 'wwwroot/index.html'))) {
    $problems += 'wwwroot/index.html is missing, so the window would open empty'
}

# The private svn client, on Windows. On Linux the .deb declares `subversion` instead.
if ($isWindowsTarget) {
    $svn = Join-Path $Output 'svn/svn.exe'
    if (-not (Test-Path $svn)) {
        if ($AllowMissingSvn) {
            Write-Host "  ! no bundled svn client - allowed by -AllowMissingSvn" -ForegroundColor Yellow
        }
        else {
            $problems += 'the bundled svn client is missing; run build/fetch-svn-tools.ps1 first, or pass -AllowMissingSvn'
        }
    }
    else {
        # Present is not the same as working: the payload is a third-party zip staged by a script, and
        # a truncated download leaves a file of the right name. The release workflow used to run this
        # as a step of its own; it belongs here, with the check that the file exists at all.
        & $svn --version --quiet | Out-Null
        if ($LASTEXITCODE -ne 0) { $problems += "the bundled svn client will not run (exit $LASTEXITCODE)" }
    }
}

if ($problems) {
    $problems | ForEach-Object { Write-Host "  x $_" -ForegroundColor Red }
    throw "the published tree is incomplete"
}

Write-Host "Layout OK: 3 tools, wwwroot, $([math]::Round((Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)) MB" -ForegroundColor Green

if ($SkipSmokeTests) {
    Write-Host "Smoke tests skipped." -ForegroundColor Yellow
    return
}

# $IsWindows is PowerShell Core only, and this repository's scripts run under Windows PowerShell 5.1
# as well - where it is simply $null, which would silently make every platform look like Linux.
$hostRuntime = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
                        [System.Runtime.InteropServices.OSPlatform]::Windows)) { 'win-x64' } else { 'linux-x64' }

if ($Runtime -ne $hostRuntime) {
    Write-Host "Smoke tests skipped: $Runtime cannot run on this $hostRuntime machine." -ForegroundColor Yellow
    return
}

# ---- each tool answers a question only a working build can answer ---------------------------------

Write-Host "Smoke testing" -ForegroundColor Cyan

function Fail($message) {
    Write-Host "  x $message" -ForegroundColor Red
    throw $message
}

# 1. The CLI prints its version.
$cli = Join-Path $Output "mlqt$exe"
$reported = & $cli --version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { Fail "mlqt --version exited $LASTEXITCODE : $reported" }
if ($reported -notmatch [regex]::Escape($Version)) {
    Fail "mlqt reported '$($reported.Trim())', which does not carry the version being published ($Version)"
}
Write-Host "  CLI        $($reported.Trim())"

# 2. The MCP server completes an initialize handshake over stdio - the same exchange a real client
#    opens with, so it proves the host builds, the DI graph resolves and the protocol answers.
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = Join-Path $Output "MLQT.McpServer$exe"
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$mcp = [System.Diagnostics.Process]::Start($psi)
try {
    $mcp.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"publish-tools","version":"1"}}}')
    $mcp.StandardInput.Flush()

    $read = $mcp.StandardOutput.ReadLineAsync()
    if (-not $read.Wait(60000)) { Fail 'the MCP server did not answer initialize within 60s' }
    $answer = $read.Result
}
finally {
    if (-not $mcp.HasExited) { $mcp.Kill() }
}

if ($answer -notmatch '"serverInfo"') { Fail "the MCP server's initialize answer had no serverInfo: $answer" }
if ($answer -notmatch [regex]::Escape($Version.Split('-')[0])) {
    Fail "the MCP server reported a different version from the one being published ($Version): $answer"
}
Write-Host "  MCP server initialize answered, serverInfo carries $Version"

# 3. The GUI runs its 16 self-test probes against this published tree and passes every one. This is
#    the whole reason /selftest exists: it is the only check that exercises the shipped layout.
$report = Join-Path ([System.IO.Path]::GetTempPath()) "mlqt-publish-selftest-$([guid]::NewGuid().ToString('N')).json"
$env:MLQT_SELFTEST = '1'
$env:MLQT_SELFTEST_HOST = 'MLQT.Photino'
$env:MLQT_SELFTEST_OUT = $report
try {
    # Start-Process rather than the call operator. Photino writes a harmless line to stderr as the
    # webview shuts down ("Failed to unregister class Chrome_WidgetWin_0"), and PowerShell turns any
    # stderr from a native command into a terminating error when ErrorActionPreference is Stop - so
    # calling it directly fails the build on a message that means nothing. The report on disk is the
    # result here; the process output is not.
    #
    # -PassThru and a bounded wait rather than -Wait: see $SelfTestTimeoutSeconds. Killed rather than
    # left running, so the tree can be deleted afterwards and the next step is not racing a webview.
    $gui = Start-Process -FilePath (Join-Path $Output "MLQT.Photino$exe") -PassThru -NoNewWindow

    if (-not $gui.WaitForExit($SelfTestTimeoutSeconds * 1000)) {
        try { $gui.Kill($true) } catch { }
        Fail "the GUI did not finish its self-test within $SelfTestTimeoutSeconds seconds; it started and never got as far as writing a report"
    }
}
finally {
    Remove-Item Env:MLQT_SELFTEST, Env:MLQT_SELFTEST_HOST, Env:MLQT_SELFTEST_OUT -ErrorAction SilentlyContinue
}

if (-not (Test-Path $report)) { Fail 'the GUI wrote no self-test report; it did not start' }

$probes = (Get-Content $report -Raw | ConvertFrom-Json).Probes
$failed = @($probes | Where-Object { $_.Status -ne 'Pass' })
Remove-Item $report -Force -ErrorAction SilentlyContinue

if ($failed.Count -gt 0) {
    $failed | ForEach-Object { Write-Host "    x $($_.Id): $($_.Detail)" -ForegroundColor Red }
    Fail "$($failed.Count) of $($probes.Count) self-test probes failed against the published tree"
}
Write-Host "  GUI        $($probes.Count)/$($probes.Count) self-test probes passed"

Write-Host "All three tools run." -ForegroundColor Green
