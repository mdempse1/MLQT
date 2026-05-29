# Bundled SVN command-line client

MLQT shells out to a private copy of the **SlikSVN** command-line client for the
SVN operations. All SVN access goes through the `svn` CLI (`svn update` is roughly an
order of magnitude faster via the CLI than the previously-used SharpSvn managed interop —
see `RevisionControl/SvnRevisionControlSystem.cs` and `RevisionControl/SvnCli.cs`).
`RevisionControl/SvnToolLocator.cs` prefers this bundled client over anything installed
system-wide.

## Layout

```
svn-tools/
  win-x64/        <- SlikSVN bin contents (svn.exe + its DLLs). NOT committed.
```

At build time `MLQT.csproj` copies `svn-tools/win-x64/**` into the app output under a
`svn/` folder, so the running app finds it at `{AppContext.BaseDirectory}/svn/svn.exe`.

## Populating the binaries

The binaries are **not** stored in source control (they are large and licensed
separately — SlikSVN is an Apache-2.0 build of Apache Subversion). Populate them with:

```pwsh
# Download + extract a SlikSVN .zip (verify the current URL at https://sliksvn.com/download/)
pwsh ../../build/fetch-svn-tools.ps1 -ZipUrl <SlikSVN-x64-zip-url>

# ...or extract a .zip you already downloaded
pwsh ../../build/fetch-svn-tools.ps1 -ZipPath .\Slik-Subversion-1.14.x-x64.zip

# ...or copy from an already-installed/extracted SlikSVN bin folder
pwsh ../../build/fetch-svn-tools.ps1 -SourceBin "C:\Program Files\SlikSvn\bin"
```

The SlikSVN .zip currently wraps an `.msi` rather than a loose `bin` folder; the script
detects this and runs an administrative MSI extract (`msiexec /a`) to unpack the binaries
without installing anything to the machine.

Use a build of Subversion **1.14 or newer** to match the working-copy format MLQT has
shipped (older clients refuse newer working copies).

## When the folder is empty

If `svn-tools/win-x64/` is empty, nothing is bundled and `SvnToolLocator` falls back to
`svn` on the system `PATH` (or the `MLQT_SVN_PATH` override). There is no managed
fallback — SVN operations require an `svn` executable to be resolvable. CI populates this
folder before publishing the release zip.
