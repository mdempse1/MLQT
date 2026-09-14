<#
.SYNOPSIS
    Renders the repository's Markdown as GitHub renders it, into local HTML you can read before
    committing.

.DESCRIPTION
    GitHub exposes its own renderer at `POST /markdown`, so this asks GitHub to render each page
    rather than approximating it with a lookalike, and pairs the result with GitHub's own
    stylesheet. Authentication comes from the `gh` CLI, which every contributor already has for
    pull requests, so there is nothing to install and no rate limit worth thinking about.

    **`mode: markdown`, not `mode: gfm`, and the difference matters.** The `gfm` mode renders text
    the way a *comment* is rendered, where every single newline becomes a `<br>`. A hard-wrapped
    paragraph then appears with its line breaks forced, which is not how GitHub renders a `.md`
    file in a repository - and it made the hard-wrapped pages look like a different style from the
    single-line ones when on GitHub they render identically. The first version of this script had
    that wrong and sent a documentation reviewer chasing an inconsistency that did not exist.

    Links are rewritten so a review is navigable: a link to another `.md` page points at the
    rendered copy of it, anchors and all, and an image points at the real file on disk. Nothing is
    copied, so the pictures you see are the pictures in the repository.

    **It sends each page's text to api.github.com to be rendered.** That is the same service that
    will host it, under the account `gh` is already signed in to, but it is a network call rather
    than local rendering - worth knowing before pointing it at anything unpublished.

.PARAMETER Output
    Where to write the HTML. Defaults to `preview-docs` under the temporary directory, which is
    outside the repository so a preview is never committed by accident.

.PARAMETER Open
    Open the index in the default browser when it has been written.

.PARAMETER Page
    Render only the pages whose file name matches this wildcard - `getting-started*`, say - when you
    are iterating on one and do not want to wait for all of them.

.EXAMPLE
    pwsh build/preview-docs.ps1 -Open

.EXAMPLE
    pwsh build/preview-docs.ps1 -Page 'code-formatting*' -Open
#>
[CmdletBinding()]
param(
    [string] $Output = (Join-Path ([System.IO.Path]::GetTempPath()) 'preview-docs'),
    [switch] $Open,
    [string] $Page = '*'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "the GitHub CLI (gh) is not installed; it is what renders the Markdown. See https://cli.github.com/"
}

# The pages, in the order a reader meets them: the user documentation, then the root pages, then the
# design notes. Design/ is included so that the links into it from Documentation/ resolve, and
# because those notes are committed Markdown that gets reviewed like any other.
$pages = @(
    Get-ChildItem -Path (Join-Path $repo 'Documentation') -Filter '*.md' | Sort-Object Name
    Get-Item -Path (Join-Path $repo 'README.md'), (Join-Path $repo 'BUILDING.md'),
                   (Join-Path $repo 'RELEASING.md'), (Join-Path $repo 'CONTRIBUTING.md'),
                   (Join-Path $repo 'CODING_GUIDELINES.md'), (Join-Path $repo 'CLAUDE.md') -ErrorAction SilentlyContinue
    Get-ChildItem -Path (Join-Path $repo 'Design') -Filter '*.md' -ErrorAction SilentlyContinue | Sort-Object Name
) | Where-Object { $_.Name -like $Page -or $_.BaseName -like $Page }

if (-not $pages) {
    throw "no Markdown matched -Page '$Page'"
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path

$shell = @'
<!doctype html><html><head><meta charset="utf-8"><title>{0}</title>
<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/github-markdown-css/5.5.1/github-markdown.min.css">
<style>
  body {{ margin:0; background:#fff; }}
  .bar {{ position:sticky; top:0; background:#f6f8fa; border-bottom:1px solid #d1d9e0;
          padding:8px 16px; font:13px -apple-system,Segoe UI,sans-serif; }}
  .bar a {{ color:#0969da; text-decoration:none; margin-right:12px; }}
  .markdown-body {{ box-sizing:border-box; max-width:1012px; margin:0 auto; padding:32px; }}
  @media (prefers-color-scheme: dark) {{ body {{ background:#0d1117; }}
    .bar {{ background:#161b22; border-color:#30363d; }} }}
</style></head><body>
<div class="bar"><a href="index.html">&#8592; all pages</a><b>{0}</b></div>
<article class="markdown-body">{1}</article></body></html>
'@

<#
    .SYNOPSIS
        Renders one page through GitHub's API, with the encoding nailed down at both ends.

    .DESCRIPTION
        **Both directions have to say UTF-8 explicitly, and neither is the default.**

        Going out: the request is written to a temporary file and handed to `gh --input`, rather than
        piped. Piping to a native executable re-encodes the text with the shell's output encoding,
        which mangles every em dash and curly quote in this repository's prose.

        Coming back: `gh` is run through `System.Diagnostics.Process` with `StandardOutputEncoding`
        set, rather than with the call operator. PowerShell decodes a native command's stdout using
        `[Console]::OutputEncoding`, which is whatever code page the *host* happens to have - so the
        same script produced clean em dashes under one console and `ÔÇö` under another, which is
        `E2 80 94` read as a DOS code page. Setting it here makes the result independent of where the
        script was launched from, without changing console state the caller may be relying on.
#>
function Invoke-GitHubMarkdown([string] $Markdown) {
    $request = [System.IO.Path]::GetTempFileName()
    try {
        $body = @{ text = $Markdown; mode = 'markdown' } | ConvertTo-Json -Depth 3 -Compress
        [System.IO.File]::WriteAllText($request, $body, [System.Text.UTF8Encoding]::new($false))

        $utf8 = [System.Text.UTF8Encoding]::new($false)
        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = 'gh'
        # Arguments as one string rather than ArgumentList, which Windows PowerShell 5.1 does not
        # have. The path is quoted because a temporary directory can contain spaces.
        $psi.Arguments = 'api -X POST markdown --input "{0}"' -f $request
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.StandardOutputEncoding = $utf8
        $psi.StandardErrorEncoding = $utf8
        $psi.UseShellExecute = $false

        $process = [System.Diagnostics.Process]::Start($psi)
        $html = $process.StandardOutput.ReadToEnd()
        $problem = $process.StandardError.ReadToEnd()
        $process.WaitForExit()

        if ($process.ExitCode -ne 0) {
            throw "gh could not render the Markdown (exit $($process.ExitCode)): $problem"
        }
        return $html
    }
    finally {
        Remove-Item $request -Force -ErrorAction SilentlyContinue
    }
}

<#
    .SYNOPSIS
        Points every relative link at something that exists in the preview.

    .DESCRIPTION
        A `.md` link becomes the rendered copy beside it, keeping any `#anchor`, so clicking through
        to another page works. Everything else - images, above all - becomes an absolute `file:` URI
        to the real file in the repository, so the preview shows the committed picture rather than a
        copy that could go stale.
#>
function Resolve-Links([string] $Html, [System.IO.FileInfo] $Source) {
    $evaluator = {
        param($match)

        $attribute = $match.Groups[1].Value
        $url = $match.Groups[2].Value

        # Anything carrying a URI scheme is somebody else's to resolve. Matched by shape rather
        # than by a list of known schemes, because GitHub *autolinks* bare URIs in prose: the
        # documentation mentions `modelica://...` as an example of a resource reference, and that
        # arrives here as an href that no file system can resolve.
        if ($url -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $url.StartsWith('#')) { return $match.Value }

        $anchor = ''
        if ($url.Contains('#')) {
            $anchor = '#' + $url.Substring($url.IndexOf('#') + 1)
            $url = $url.Substring(0, $url.IndexOf('#'))
        }
        if (-not $url) { return $match.Value }

        try {
            $decoded = [System.Uri]::UnescapeDataString($url)
            $target = [System.IO.Path]::GetFullPath((Join-Path $Source.DirectoryName $decoded))
        }
        catch {
            # Not a path this machine can make sense of. Leave the link exactly as GitHub wrote it
            # rather than failing the whole preview over one link in one page.
            return $match.Value
        }

        if ($url.EndsWith('.md')) {
            return '{0}="{1}.html{2}"' -f $attribute, [System.IO.Path]::GetFileNameWithoutExtension($target), $anchor
        }
        return '{0}="{1}{2}"' -f $attribute, ([System.Uri]$target).AbsoluteUri, $anchor
    }

    return [regex]::Replace($Html, '\b(href|src)="([^"]+)"', $evaluator)
}

# $doc, not $page: the parameter above is [string] $Page, PowerShell variable names are
# case-insensitive, and assigning a FileInfo to a typed string variable coerces it silently - so the
# loop variable became a path string and $page.FullName was empty.
$written = @()
foreach ($doc in $pages) {
    $html = Resolve-Links (Invoke-GitHubMarkdown ([System.IO.File]::ReadAllText($doc.FullName))) $doc
    $file = Join-Path $Output ($doc.BaseName + '.html')
    [System.IO.File]::WriteAllText($file, ($shell -f $doc.Name, $html), [System.Text.UTF8Encoding]::new($false))
    $written += $doc
    Write-Host "  $($doc.Name)"
}

$links = ($written | ForEach-Object {
    $relative = $_.FullName.Substring($repo.Length + 1).Replace('\', '/')
    '<li><a href="{0}.html">{1}</a></li>' -f $_.BaseName, $relative
}) -join "`n"

$index = Join-Path $Output 'index.html'
[System.IO.File]::WriteAllText(
    $index,
    ($shell -f 'MLQT documentation preview', "<h1>Pages</h1><ul>$links</ul>"),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "`n$($written.Count) page(s) -> $index" -ForegroundColor Green

if ($Open) {
    Start-Process $index
}
