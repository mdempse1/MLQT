#!/bin/bash
#
# MLQT — Linux installer (.deb)
#
# Phase 7b-7. One installer per platform carrying all three tools: the Photino GUI, the `mlqt` CLI
# and the MCP server. This is the Linux half; build/installer/mlqt.iss is the Windows one, and both
# package the same staging tree that build/publish-tools.ps1 produces and smoke-tests.
#
#   pwsh build/publish-tools.ps1 -Runtime linux-x64 -SelfContained -Version 1.2.3 -Output publish/linux-x64
#   build/package-deb.sh --version 1.2.3 --stage publish/linux-x64 --output artifacts
#
# On a machine with no display - a CI runner - run the whole script under `xvfb-run -a` so that the
# last smoke test, which starts the GUI, runs rather than being skipped. It is the strongest check
# here and skipping it is how a package ships a window that opens on nothing.
#
# Shell rather than PowerShell, unlike everything else in build/. dpkg-deb exists only on a Debian
# machine, and a packaging script you cannot run on the machine that makes the package is the wrong
# trade — pwsh is not installed on a plain Ubuntu desktop, and this repository's Linux development
# box does not have it. The Windows counterpart is an Inno Setup script for the same reason.
#
# Layout, and why:
#
#   /opt/mlqt/                        the published tree, whole. Three applications sharing every
#                                     assembly below MLQT.Shared, so one copy rather than three
#   /usr/bin/mlqt                     symlink — the CLI, which is what CI and the pre-commit hook run
#   /usr/bin/mlqt-mcp-server          symlink — a stable path, because agents register the server by
#                                     path and /opt/mlqt is not on anybody's PATH
#   /usr/bin/mlqt-gui                 a wrapper, NOT a symlink; see build/packaging/linux/mlqt-gui
#   /usr/share/applications/MLQT.Photino.desktop
#   /usr/share/icons/hicolor/*/apps/mlqt.png
#
# The desktop entry and the icons are not polish. On a Wayland session Photino's SetIconFile is a
# no-op — there is no protocol for a client to give its own window an icon — so the shell matches the
# window's app_id to an installed desktop entry and takes the icon from there, for the dock, Alt-Tab
# and the window list alike. Without this package MLQT has no icon anywhere on Wayland (B134).
#
set -euo pipefail

version=""
stage=""
output="artifacts"
arch="amd64"
skip_smoke_tests=0

usage() {
    cat <<'USAGE'
Usage: build/package-deb.sh --stage <dir> [options]

  --version <v>     Package version. Default: read from the staged mlqt binary.
  --stage <dir>     The publish tree to package (required).
  --output <dir>    Where to write the .deb. Default: artifacts
  --arch <arch>     Debian architecture. Default: amd64
  --skip-smoke-tests
USAGE
}

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version="$2"; shift 2 ;;
        --stage)   stage="$2";   shift 2 ;;
        --output)  output="$2";  shift 2 ;;
        --arch)    arch="$2";    shift 2 ;;
        --skip-smoke-tests) skip_smoke_tests=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
packaging="$repo/build/packaging/linux"

[ -n "$stage" ] || { echo "--stage is required" >&2; exit 2; }
[ -d "$stage" ] || { echo "no such staging tree: $stage" >&2; exit 1; }
stage="$(cd "$stage" && pwd)"

for tool in dpkg-deb fakeroot; do
    command -v "$tool" >/dev/null 2>&1 || { echo "$tool is not installed" >&2; exit 1; }
done

# ---- the tree is what it claims to be -------------------------------------------------------------
#
# The same guard the Inno script makes at compile time, and for the same reason: the payload is
# copied wholesale, so a publish that quietly stopped producing one of the three would package
# without it and nothing would say so until a user went looking.

problems=()
for f in MLQT.Photino mlqt MLQT.McpServer; do
    [ -f "$stage/$f" ] || problems+=("$f is missing from the staging tree")
done
# Without this the window opens and loads nothing at all, with no error (B133).
[ -f "$stage/wwwroot/index.html" ] || problems+=("wwwroot/index.html is missing, so the window would open empty")

if [ ${#problems[@]} -gt 0 ]; then
    printf '  x %s\n' "${problems[@]}" >&2
    echo "the staging tree is incomplete; run build/publish-tools.ps1 first" >&2
    exit 1
fi

if [ -z "$version" ]; then
    version="$("$stage/mlqt" --version | awk '{print $2}' | cut -d+ -f1)"
    echo "Version from the staged CLI: $version"
fi

# Debian versions may not carry a '+' or a '~' from a semver suffix in the wrong place; '-dev' is a
# Debian revision separator. Normalise the way a semver pre-release maps: 1.2.3-rc1 -> 1.2.3~rc1,
# which sorts *before* 1.2.3 exactly as the semver does.
deb_version="$(printf '%s' "$version" | sed 's/+.*//; s/-/~/')"

# The icon sizes the package installs, and the ones its smoke test looks for. One list, because
# written twice they drift.
icon_sizes="16 24 32 48 256 512"

# ---- the dependencies, which are read from the binaries rather than remembered --------------------
#
# Only the direct DT_NEEDED entries of the one native library that has any: everything else in the
# tree is managed code or a .NET runtime library that links nothing outside libc. Declaring the
# direct set rather than trusting webkit's own dependency graph matters for at least one of them —
# libnotify4 is NOT pulled in by libwebkit2gtk-4.1-0, and Photino.Native links it, so a machine
# without it gets a GUI that cannot load its native library at all. Not a guess: it is the reason the
# desktop-selftest job failed the first time it ran on Linux, on a runner that had webkit. Every
# developer desktop has libnotify4 for unrelated reasons, so nothing else was ever going to find it.
#
# Unversioned on purpose. A version from dpkg-shlibdeps would pin the package to the distribution it
# was built on; the floor here is webkit2gtk 4.1 + GTK3, which is Ubuntu 22.04 and Debian 12, and
# below that no version constraint helps because the ABI is simply not there. libgtk-3-0 and
# libglib2.0-0 are the pre-t64 names, which the t64 packages on 24.04 and later still Provide — so
# these names are the ones that resolve on both sides of that transition.
depends="libwebkit2gtk-4.1-0, libjavascriptcoregtk-4.1-0, libgtk-3-0, libglib2.0-0, libnotify4, libstdc++6, libgcc-s1, libc6"

# ---- lay the tree out ------------------------------------------------------------------------------

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
root="$work/root"

install -d "$root/opt/mlqt" "$root/usr/bin" "$root/usr/share/applications" \
           "$root/usr/share/doc/mlqt" "$root/DEBIAN"

cp -a "$stage/." "$root/opt/mlqt/"

# dpkg is unhappy about world-writable files and about a payload owned by the building user; the
# ownership is set by fakeroot at build time, the modes here.
find "$root/opt/mlqt" -type d -exec chmod 755 {} +
find "$root/opt/mlqt" -type f -exec chmod 644 {} +
chmod 755 "$root/opt/mlqt/MLQT.Photino" "$root/opt/mlqt/mlqt" "$root/opt/mlqt/MLQT.McpServer"
find "$root/opt/mlqt" -type f -name '*.so' -exec chmod 755 {} +

ln -s /opt/mlqt/mlqt           "$root/usr/bin/mlqt"
ln -s /opt/mlqt/MLQT.McpServer "$root/usr/bin/mlqt-mcp-server"
install -m 755 "$packaging/mlqt-gui" "$root/usr/bin/mlqt-gui"

install -m 644 "$packaging/MLQT.Photino.desktop" "$root/usr/share/applications/MLQT.Photino.desktop"

# The icon theme. Named `mlqt` because it is a theme name rather than a file path — the Icon= key in
# the desktop entry names it and the shell picks the size it wants. Every size the designer supplied
# is installed: a dock at 2x wants 96 or 128 and will scale the nearest, and giving it 512 to scale
# down looks better than giving it 48 to scale up.
for size in $icon_sizes; do
    src="$repo/Branding/mlqt-$size.png"
    [ -f "$src" ] || { echo "missing branding asset: $src" >&2; exit 1; }
    install -D -m 644 "$src" "$root/usr/share/icons/hicolor/${size}x${size}/apps/mlqt.png"
done

install -m 644 "$packaging/copyright" "$root/usr/share/doc/mlqt/copyright"

# ---- control ---------------------------------------------------------------------------------------

installed_size="$(du -sk "$root" | cut -f1)"

sed -e "s/@VERSION@/$deb_version/" \
    -e "s/@ARCH@/$arch/" \
    -e "s/@INSTALLED_SIZE@/$installed_size/" \
    -e "s|@DEPENDS@|$depends|" \
    "$packaging/control.in" > "$root/DEBIAN/control"

install -m 755 "$packaging/postinst" "$root/DEBIAN/postinst"
install -m 755 "$packaging/postrm"   "$root/DEBIAN/postrm"

mkdir -p "$output"
output="$(cd "$output" && pwd)"
deb="$output/mlqt_${deb_version}_${arch}.deb"

echo "Building $deb"
fakeroot dpkg-deb --root-owner-group -Zxz --build "$root" "$deb" >/dev/null

echo "Built $(du -h "$deb" | cut -f1) from $((installed_size / 1024)) MB installed"

if [ "$skip_smoke_tests" = 1 ]; then
    echo "Smoke tests skipped."
    exit 0
fi

# ---- the package answers questions only a built package can ----------------------------------------
#
# publish-tools.ps1 proves the three tools run; this proves the package puts them somewhere they can
# still run from, with the desktop entry and the icons a Wayland session needs. Extracting rather
# than installing, so it needs no root and runs on a CI runner.

echo "Smoke testing"

extract="$work/extract"
dpkg-deb -x "$deb" "$extract"

fail() { echo "  x $1" >&2; exit 1; }

# 1. dpkg itself is satisfied with the control file. `dpkg-deb --info` parses it, and a malformed
#    field is the sort of thing that only shows up when a user runs apt.
#
#    Nothing here checks the maintainer scripts' permissions, and that is deliberate: `dpkg-deb
#    --build` above refuses outright ("maintainer script 'postinst' has bad permissions 664"), so a
#    check of it here could never fire. Measured, by replacing the `install -m 755` with a `cp` and
#    watching the build - not this section - reject it.
dpkg-deb --info "$deb" >/dev/null || fail "dpkg cannot read the package it just built"

# 2. Every symlink resolves inside the package. A symlink to a path that does not exist installs
#    perfectly and fails the first time it is used.
for link in mlqt mlqt-mcp-server; do
    target="$(readlink "$extract/usr/bin/$link")"
    [ -f "$extract$target" ] || fail "/usr/bin/$link points at $target, which the package does not contain"
done
grep -q '^exec -a MLQT.Photino /opt/mlqt/MLQT.Photino' "$extract/usr/bin/mlqt-gui" \
    || fail "/usr/bin/mlqt-gui no longer launches the GUI under its own argv[0]"

# 3. The desktop entry is valid, and its Exec names the real binary. Launching through a symlink or
#    a differently-named wrapper changes the window's app_id, the entry then matches nothing, and
#    the icon silently disappears. Measured; see build/packaging/linux/mlqt-gui.
if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$extract/usr/share/applications/MLQT.Photino.desktop" \
        || fail "the desktop entry is not valid"
fi
grep -q '^Exec=/opt/mlqt/MLQT.Photino$' "$extract/usr/share/applications/MLQT.Photino.desktop" \
    || fail "the desktop entry's Exec no longer names the binary whose app_id it has to match"

# 4. The icon the entry names is actually installed, at every size claimed.
icon="$(sed -n 's/^Icon=//p' "$extract/usr/share/applications/MLQT.Photino.desktop")"
for size in $icon_sizes; do
    [ -f "$extract/usr/share/icons/hicolor/${size}x${size}/apps/$icon.png" ] \
        || fail "the entry names icon '$icon' but ${size}x${size} is not in the package"
done

# 5. The CLI runs from where the package puts it, and says the version the package claims.
reported="$("$extract/opt/mlqt/mlqt" --version 2>&1)" || fail "mlqt --version failed: $reported"
case "$reported" in
    *"$version"*) ;;
    *) fail "mlqt reported '$reported', which does not carry $version" ;;
esac
echo "  CLI        $reported"

# 6. The MCP server answers an initialize handshake over stdio, launched through a symlink of the
#    same shape as the one the package installs — which also proves .NET resolves its assemblies
#    from the real path rather than from argv[0]. Not through /usr/bin/mlqt-mcp-server itself: that
#    symlink is absolute, so in an extracted tree it points at a /opt/mlqt that does not exist yet.
#    Running it there fails with "No such file or directory" and looks exactly like a broken binary.
#
install -d "$work/link"
ln -sf "$extract/opt/mlqt/MLQT.McpServer" "$work/link/mlqt-mcp-server"
#
# Through a fifo, and not simply `printf ... | server`. A pipe that closes as soon as the request
# has been written ends the session before the server has answered — the transport logs "completed
# reading messages" and exits 0 with nothing on stdout, which reads exactly like a server that
# cannot start. Holding the input open until a line comes back is the difference, and `head -1`
# closes it as soon as one does, so this costs no wall-clock at all.
fifo="$work/mcp.in"
mkfifo "$fifo"
{ printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"package-deb","version":"1"}}}'
  sleep 60; } > "$fifo" &
writer=$!
answer="$(timeout 60 "$work/link/mlqt-mcp-server" < "$fifo" 2>/dev/null | head -1)" || true
kill "$writer" 2>/dev/null || true
case "$answer" in
    *'"serverInfo"'*) ;;
    *) fail "the MCP server's initialize answer had no serverInfo: ${answer:-<nothing>}" ;;
esac
case "$answer" in
    *"${version%%-*}"*) ;;
    *) fail "the MCP server reported a different version from the one being packaged ($version)" ;;
esac
echo "  MCP server initialize answered through a symlink, carrying $version"

# 7. The GUI runs the 16 /selftest probes from the packaged tree. The strongest check in this
#    repository: it resolves the RCL assets, runs interop, renders MudBlazor and exercises settings,
#    logging and the svn locator, in the layout that ships. Skipped where there is no display, which
#    is every CI runner unless one is arranged.
if [ -n "${DISPLAY:-}" ] || [ -n "${WAYLAND_DISPLAY:-}" ]; then
    report="$work/selftest.json"
    MLQT_SELFTEST=1 MLQT_SELFTEST_HOST=MLQT.Photino MLQT_SELFTEST_OUT="$report" \
        "$extract/opt/mlqt/MLQT.Photino" >/dev/null 2>&1 || true

    [ -f "$report" ] || fail "the GUI wrote no self-test report; it did not start"

    python3 - "$report" <<'PY' || exit 1
import json, sys
probes = json.load(open(sys.argv[1]))["Probes"]
failed = [p for p in probes if p["Status"] != "Pass"]
for p in failed:
    print(f"    x {p['Id']}: {p.get('Detail')}")
print(f"  GUI        {len(probes) - len(failed)}/{len(probes)} self-test probes passed")
sys.exit(1 if failed else 0)
PY
else
    echo "  GUI        self-test skipped: no display on this machine"
fi

echo "The package installs three tools that run, and a desktop entry that matches the window."
