#!/bin/bash
#
# MLQT — publish the three shipping tools into one tree, then prove each of them runs.
#
# Phase 7b-7. One installer per platform carries the Photino GUI, the `mlqt` CLI and the MCP server,
# so all three are published into a single directory: they are built from one repository, share every
# assembly below MLQT.Shared, and each carries its own .deps.json and .runtimeconfig.json. Publishing
# them together costs one copy of the shared assemblies rather than three.
#
#   build/publish-tools.sh --version 1.2.3 --output publish/win-x64 --allow-missing-svn
#   build/publish-tools.sh --runtime linux-x64 --self-contained --version 1.2.3 --output publish/linux-x64
#   build/package-deb.sh   --version 1.2.3 --stage publish/linux-x64 --output artifacts
#
# **The smoke tests are the point of this script.** Building an installer around a tree nobody has run
# is how you ship an application that does not start, and this phase has already found two of those by
# running things: the Photino host that resolved no web assets outside a publish (B133), and the one
# that shipped no svn client (B144). So each tool is asked a question only a working build can answer:
#
#   mlqt          prints its version and exits 0
#   MCP server    answers an MCP `initialize` handshake over stdio with its own name and version
#   GUI           runs the /selftest probes against the published tree and passes every one
#
# The last is the strongest thing available: it resolves the RCL assets, runs JavaScript interop,
# renders MudBlazor, and exercises settings, logging, the file picker and the svn locator — in the
# published layout, which is the layout that ships. On a machine with no display, run this whole
# script under `xvfb-run -a` so that check runs rather than being skipped.
#
# Shell rather than PowerShell, and this script is why build/package-deb.sh was not enough on its own.
# That script is shell because dpkg-deb exists only on a Debian machine and pwsh is not installed on a
# plain Ubuntu desktop — but it packages *this* script's output and tells you to run it first, so a
# PowerShell publish step put the requirement straight back and made the Linux half of the build
# unrunnable on an ordinary Linux box. The Windows counterpart of package-deb.sh is an Inno Setup
# script; the counterpart of this one is this one, under Git Bash.
#
set -euo pipefail

runtime="win-x64"
version="0.0.0-dev"
output=""
configuration="Release"
self_contained=0
allow_missing_svn=0
skip_smoke_tests=0

# How long the GUI gets to run its probes and exit. Bounded because the failure it guards is a host
# that starts and never renders: it does not crash, it waits, and an unbounded wait hands a CI job its
# whole timeout — six hours of runner, reporting nothing about why (B146). Generous, because a cold
# first run on a slow runner does real work: WebView2 or WebKitGTK starts, the RCL assets resolve and
# every probe runs.
self_test_timeout="${MLQT_SELFTEST_TIMEOUT:-300}"

usage() {
    cat <<'USAGE'
Usage: build/publish-tools.sh --output <dir> [options]

  --output <dir>            Where to publish the three tools (required).
  --runtime <rid>           win-x64 | linux-x64 | win-arm64 | linux-arm64. Default: win-x64
  --version <v>             Version stamped into all three tools. Default: 0.0.0-dev
  --configuration <c>       Default: Release
  --self-contained          Bundle the .NET runtime. The .deb does; the Windows installer does not.
  --allow-missing-svn       Do not fail when the bundled svn client is absent (Windows target).
  --skip-smoke-tests        Publish and check the layout, but do not run the three tools.
  --self-test-timeout <s>   Seconds the GUI gets to run its probes. Default: 300
USAGE
}

while [ $# -gt 0 ]; do
    case "$1" in
        --output)            output="$2";              shift 2 ;;
        --runtime)           runtime="$2";             shift 2 ;;
        --version)           version="$2";             shift 2 ;;
        --configuration)     configuration="$2";       shift 2 ;;
        --self-test-timeout) self_test_timeout="$2";   shift 2 ;;
        --self-contained)    self_contained=1;         shift ;;
        --allow-missing-svn) allow_missing_svn=1;      shift ;;
        --skip-smoke-tests)  skip_smoke_tests=1;       shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

[ -n "$output" ] || { echo "--output is required" >&2; usage >&2; exit 2; }

case "$runtime" in
    win-x64|linux-x64|win-arm64|linux-arm64) ;;
    *) echo "unknown runtime: $runtime (win-x64, linux-x64, win-arm64, linux-arm64)" >&2; exit 2 ;;
esac

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

case "$runtime" in
    win-*) windows_target=1; exe=".exe" ;;
    *)     windows_target=0; exe="" ;;
esac

# The three tools, and the file each must leave behind. One list: the layout check below and the
# Windows installer's own guard both read it, and written twice they drift.
tools_project=(
    "MLQT.Photino/MLQT.Photino.csproj"
    "MLQT.Cli/MLQT.Cli.csproj"
    "MLQT.McpServer/MLQT.McpServer.csproj"
)
tools_file=("MLQT.Photino$exe" "mlqt$exe" "MLQT.McpServer$exe")
tools_name=("GUI" "CLI" "MCP server")

rm -rf "$output"
mkdir -p "$output"
output="$(cd "$output" && pwd)"

echo "Publishing 3 tools for $runtime at $version"

for i in "${!tools_project[@]}"; do
    echo "  ${tools_name[$i]}"
    dotnet publish "$repo/${tools_project[$i]}" \
        -c "$configuration" \
        -r "$runtime" \
        --self-contained "$([ "$self_contained" -eq 1 ] && echo true || echo false)" \
        -p:Version="$version" \
        -o "$output" \
        --nologo -v quiet \
        || { echo "publishing ${tools_name[$i]} failed" >&2; exit 1; }
done

# ---- the tree is what it claims to be -------------------------------------------------------------

problems=()

for i in "${!tools_file[@]}"; do
    [ -f "$output/${tools_file[$i]}" ] || problems+=("${tools_name[$i]} is missing: ${tools_file[$i]}")
done

# The host serves index.html and the RCL assets from here. Without it the window opens and loads
# nothing at all, with no error — which is what a built-but-not-published tree does (B133).
[ -f "$output/wwwroot/index.html" ] \
    || problems+=("wwwroot/index.html is missing, so the window would open empty")

# The MCP server's diagram renderer rasterises through SkiaSharp, whose real work is in a native
# library published per runtime identifier (B196). It is not loaded until get_diagram_image is
# called, so a tree missing it builds, publishes, installs and answers every other tool - which is
# B133 and B144's shape exactly, and the reason this script exists.
skia="$output/libSkiaSharp$([ "$windows_target" -eq 1 ] && echo .dll || echo .so)"
[ -f "$skia" ] || problems+=("libSkiaSharp is missing for $runtime, so get_diagram_image would fail at first use")

# The private svn client, on Windows. On Linux the .deb declares `subversion` instead.
if [ "$windows_target" -eq 1 ]; then
    svn="$output/svn/svn.exe"
    if [ ! -f "$svn" ]; then
        if [ "$allow_missing_svn" -eq 1 ]; then
            echo "  ! no bundled svn client - allowed by --allow-missing-svn"
        else
            problems+=("the bundled svn client is missing; run build/fetch-svn-tools.ps1 first, or pass --allow-missing-svn")
        fi
    else
        # Present is not the same as working: the payload is a third-party zip staged by a script, and
        # a truncated download leaves a file of the right name. The release workflow used to run this
        # as a step of its own; it belongs here, with the check that the file exists at all.
        "$svn" --version --quiet >/dev/null 2>&1 \
            || problems+=("the bundled svn client will not run")
    fi
fi

if [ ${#problems[@]} -gt 0 ]; then
    printf '  x %s\n' "${problems[@]}" >&2
    echo "the published tree is incomplete" >&2
    exit 1
fi

echo "Layout OK: 3 tools, wwwroot, $(du -sm "$output" | cut -f1) MB"

if [ "$skip_smoke_tests" -eq 1 ]; then
    echo "Smoke tests skipped."
    exit 0
fi

case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) host_runtime="win" ;;
    *)                    host_runtime="linux" ;;
esac
case "$runtime" in "$host_runtime"-*) ;; *)
    echo "Smoke tests skipped: $runtime cannot run on this $host_runtime machine."
    exit 0 ;;
esac

# ---- each tool answers a question only a working build can answer ---------------------------------

echo "Smoke testing"

fail() { echo "  x $1" >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# 1. The CLI prints its version.
reported="$("$output/mlqt$exe" --version 2>&1)" || fail "mlqt --version failed: $reported"
case "$reported" in
    *"$version"*) ;;
    *) fail "mlqt reported '$reported', which does not carry the version being published ($version)" ;;
esac
echo "  CLI        $reported"

# 2. The MCP server completes an initialize handshake over stdio — the same exchange a real client
#    opens with, so it proves the host builds, the DI graph resolves and the protocol answers.
#
#    Not simply `printf ... | server`. A pipe that closes as soon as the request has been written ends
#    the session before the server has answered — the transport logs "completed reading messages" and
#    exits 0 with nothing on stdout, which reads exactly like a server that cannot start. So the
#    writer stays alive until an answer appears, and then stops, which closes stdin and lets the
#    server exit on its own.
#
#    An ordinary pipe rather than the fifo build/package-deb.sh uses, because this script also runs
#    on Windows under Git Bash, where `mkfifo` makes an MSYS-emulated fifo that a native .exe cannot
#    read from at all — the handshake simply returns nothing. A bash pipeline is a real OS pipe on
#    both platforms. Polling the server's own output is also what keeps this quick: waiting out a
#    `sleep` instead costs the full timeout on every publish.
mcp_out="$work/mcp.out"
{ printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"publish-tools","version":"1"}}}'
  for _ in $(seq 1 600); do [ -s "$mcp_out" ] && break; sleep 0.1; done
} | timeout 60 "$output/MLQT.McpServer$exe" > "$mcp_out" 2>/dev/null || true
answer="$(head -1 "$mcp_out" 2>/dev/null || true)"

case "$answer" in
    *'"serverInfo"'*) ;;
    *) fail "the MCP server's initialize answer had no serverInfo: ${answer:-<nothing>}" ;;
esac
case "$answer" in
    *"${version%%-*}"*) ;;
    *) fail "the MCP server reported a different version from the one being published ($version)" ;;
esac
echo "  MCP server initialize answered, serverInfo carries $version"

# 3. The GUI runs its /selftest probes against this published tree and passes every one. This is the
#    whole reason /selftest exists: it is the only check that exercises the shipped layout.
#
#    Skipped where there is no display, which is every CI runner unless one is arranged — run the
#    script under `xvfb-run -a` and it runs. A Windows machine always has one.
if [ "$host_runtime" = "win" ] || [ -n "${DISPLAY:-}" ] || [ -n "${WAYLAND_DISPLAY:-}" ]; then
    report="$work/selftest.json"

    # The exit code is the answer — 0 when every probe passed, 1 when any failed, 2 when the report
    # could not be written (MLQT.Shared/Pages/SelfTest.razor.cs says so, and says a capture script
    # should not have to parse the file to know). `timeout` bounds it for the reason above; a run it
    # had to kill leaves no report, which is then the failure.
    status=0
    MLQT_SELFTEST=1 MLQT_SELFTEST_HOST=MLQT.Photino MLQT_SELFTEST_OUT="$report" \
        timeout "$self_test_timeout" "$output/MLQT.Photino$exe" >/dev/null 2>&1 || status=$?

    [ -f "$report" ] \
        || fail "the GUI wrote no self-test report within ${self_test_timeout}s; it started and never got as far as writing one"

    # The counts, for the line a person reads. Skipped is neither a pass nor a failure, so it is
    # named rather than folded into either — the exit code above has already decided the outcome.
    total=$(grep -c '"Id":' "$report" || true)
    failed=$(grep -c '"Status": "Fail"' "$report" || true)
    skipped=$(grep -c '"Status": "Skipped"' "$report" || true)

    if [ "$status" -ne 0 ] || [ "$failed" -gt 0 ]; then
        grep -B2 '"Status": "Fail"' "$report" | sed -n 's/.*"Id": "\([^"]*\)".*/    x \1/p' >&2 || true
        fail "$failed of $total self-test probes failed against the published tree (exit $status)"
    fi

    echo "  GUI        $((total - skipped))/$total self-test probes passed$([ "$skipped" -gt 0 ] && echo ", $skipped skipped")"
else
    echo "  GUI        self-test skipped: no display on this machine"
fi

echo "All three tools run."
