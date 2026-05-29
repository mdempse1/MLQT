<#
.SYNOPSIS
    Downloads the SlikSVN command-line client and stages its binaries so they can be
    bundled with the MLQT application.

.DESCRIPTION
    MLQT shells out to a private copy of the `svn` command-line client for the
    performance-critical working-copy operations (see RevisionControl/SvnToolLocator.cs).
    Rather than require every user to install an SVN client, we bundle the SlikSVN
    command-line build into the app under a `svn/` folder next to the executable.

    This script obtains the SlikSVN binaries and copies them into
    MLQT/MLQT/svn-tools/win-x64/, which MLQT.csproj then copies into the build output
    under `svn/`. Run it once locally before publishing, and in CI before `dotnet publish`.

    The binaries are NOT committed to source control (see .gitignore); this script is the
    reproducible way to (re)populate them.

    SlikSVN distributes the client as a .zip archive. The current downloads wrap an .msi
    *inside* that zip rather than shipping a loose `bin` folder, so this script extracts the
    zip and, if it doesn't find svn.exe directly, performs an administrative MSI extract
    (`msiexec /a`) to unpack the payload without installing anything to the machine.
    Acquisition strategy (in order):
      1. -SourceBin <dir>   : copy an already-extracted SlikSVN `bin` folder. Use this for
                              air-gapped / fully reproducible builds where the client is
                              vendored elsewhere.
      2. -ZipPath <file>    : extract a SlikSVN .zip you have already downloaded.
      3. -ZipUrl <url>      : download a SlikSVN .zip, then extract it.

.PARAMETER OutDir
    Destination for the staged binaries. Defaults to MLQT/MLQT/svn-tools/win-x64
    relative to this script.

.PARAMETER ZipUrl
    URL of the SlikSVN 64-bit .zip to download. The exact URL/version changes over time -
    verify the current download link at https://sliksvn.com/download/ and pass it
    explicitly (or set $env:MLQT_SLIKSVN_ZIP_URL). Must be SVN >= 1.14 to match the
    working-copy format written by the SharpSvn 1.14 builds MLQT has shipped.

.PARAMETER ZipPath
    Path to a pre-downloaded SlikSVN .zip to extract instead of downloading.

.PARAMETER SourceBin
    Path to an already-extracted SlikSVN `bin` directory to copy verbatim.

.EXAMPLE
    pwsh build/fetch-svn-tools.ps1 -ZipUrl https://sliksvn.com/.../Slik-Subversion-1.14.x-x64.zip

.EXAMPLE
    pwsh build/fetch-svn-tools.ps1 -ZipPath .\Slik-Subversion-1.14.5-x64.zip

.EXAMPLE
    pwsh build/fetch-svn-tools.ps1 -SourceBin "C:\Program Files\SlikSvn\bin"
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$ZipUrl = $env:MLQT_SLIKSVN_ZIP_URL,
    [string]$ZipPath,
    [string]$SourceBin
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) {
    $OutDir = Join-Path $scriptDir '..\MLQT\svn-tools\win-x64'
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

function Copy-Bin([string]$binDir) {
    if (-not (Test-Path $binDir)) {
        throw "SVN bin directory not found: $binDir"
    }
    $svnExe = Join-Path $binDir 'svn.exe'
    if (-not (Test-Path $svnExe)) {
        throw "svn.exe not found in $binDir - is this a SlikSVN bin folder?"
    }

    if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    # Copy the whole bin folder: svn.exe depends on sibling DLLs (libsvn, APR, SQLite,
    # zlib, OpenSSL, serf, intl, ...). Copying everything is simpler and safer than
    # cherry-picking, and the extra CLI tools (svnadmin etc.) are tiny.
    Copy-Item -Path (Join-Path $binDir '*') -Destination $OutDir -Recurse -Force

    $version = (& $svnExe --version --quiet) 2>$null
    Write-Host "Staged SlikSVN $version into $OutDir"
    Write-Host ("Files: {0}" -f (Get-ChildItem -Recurse -File $OutDir).Count)
}

if ($SourceBin) {
    Copy-Bin ([System.IO.Path]::GetFullPath($SourceBin))
    return
}

# Need a zip: either supplied or downloaded.
$tempZip = $null
$extractDir = $null
$msiDir = $null
try {
    if (-not $ZipPath) {
        if (-not $ZipUrl) {
            throw "No source provided. Pass -SourceBin, -ZipPath, or -ZipUrl " +
                  "(or set `$env:MLQT_SLIKSVN_ZIP_URL). Find the current .zip URL at " +
                  "https://sliksvn.com/download/."
        }
        $tempZip = Join-Path ([System.IO.Path]::GetTempPath()) ("sliksvn-" + [guid]::NewGuid() + ".zip")
        Write-Host "Downloading SlikSVN zip from $ZipUrl"
        Invoke-WebRequest -Uri $ZipUrl -OutFile $tempZip -UseBasicParsing
        $ZipPath = $tempZip
    }
    $ZipPath = [System.IO.Path]::GetFullPath($ZipPath)
    if (-not (Test-Path $ZipPath)) { throw "Zip not found: $ZipPath" }

    $extractDir = Join-Path ([System.IO.Path]::GetTempPath()) ("sliksvn-extract-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
    Write-Host "Extracting $ZipPath to $extractDir"
    Expand-Archive -Path $ZipPath -DestinationPath $extractDir -Force

    # The SlikSVN zip nests the client under a top-level folder (e.g.
    # Slik-Subversion-1.14.x-x64\bin\), and the exact prefix varies between versions, so
    # locate the bin folder by finding svn.exe.
    $svn = Get-ChildItem -Path $extractDir -Recurse -Filter 'svn.exe' -File |
           Select-Object -First 1

    # Current SlikSVN downloads ship an .msi *inside* the zip rather than a loose bin
    # folder. If we didn't find svn.exe directly, locate the MSI and unpack it with an
    # administrative install (msiexec /a), which lays the payload out on disk (including
    # bin\svn.exe) without actually installing anything to the machine.
    if (-not $svn) {
        $msi = Get-ChildItem -Path $extractDir -Recurse -Filter '*.msi' -File |
               Select-Object -First 1
        if (-not $msi) {
            throw "Neither svn.exe nor an .msi was found in the extracted zip tree under $extractDir"
        }
        $msiDir = Join-Path ([System.IO.Path]::GetTempPath()) ("sliksvn-msi-" + [guid]::NewGuid())
        New-Item -ItemType Directory -Force -Path $msiDir | Out-Null
        Write-Host "Found MSI $($msi.FullName); performing administrative extract to $msiDir"
        $proc = Start-Process -FilePath 'msiexec.exe' `
            -ArgumentList @('/a', "`"$($msi.FullName)`"", '/qn', "TARGETDIR=`"$msiDir`"") `
            -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw "msiexec administrative extract failed with exit code $($proc.ExitCode)"
        }
        $svn = Get-ChildItem -Path $msiDir -Recurse -Filter 'svn.exe' -File |
               Select-Object -First 1
        if (-not $svn) {
            throw "svn.exe not found after extracting MSI $($msi.FullName) to $msiDir"
        }
    }

    Copy-Bin $svn.Directory.FullName
}
finally {
    if ($extractDir -and (Test-Path $extractDir)) {
        Remove-Item -Recurse -Force $extractDir -ErrorAction SilentlyContinue
    }
    if ($msiDir -and (Test-Path $msiDir)) {
        Remove-Item -Recurse -Force $msiDir -ErrorAction SilentlyContinue
    }
    if ($tempZip -and (Test-Path $tempZip)) {
        Remove-Item -Force $tempZip -ErrorAction SilentlyContinue
    }
}
