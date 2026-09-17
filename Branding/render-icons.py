#!/usr/bin/env python3
"""Regenerate the application icon rasters from the two SVG sources.

    python3 Branding/render-icons.py

Writes mlqt-16/24/32/48/256/512.png and mlqt.ico, beside the sources. Run it after editing
mlqt-app-icon.svg or mlqt-app-icon-16.svg; nothing else in the repository regenerates them, and a
project that copies them (MLQT.Photino.csproj, package-deb.sh, mlqt.iss) takes whatever is here.

16px comes from its own source. See the comment in mlqt-app-icon-16.svg for why.

Needs librsvg's gdk-pixbuf loader and the GI bindings, which a GTK desktop already has:
    sudo apt install python3-gi gir1.2-gdkpixbuf-2.0 librsvg2-common
The .ico is assembled here rather than by a tool, so that every frame stays PNG-compressed, as the
designer's original was: ImageMagick writes the 256px frame as uncompressed BMP, which alone is
256KB and takes the file to 281KB against the 10KB it is now.
"""
import pathlib
import struct
import sys

import gi
gi.require_version("GdkPixbuf", "2.0")
from gi.repository import GdkPixbuf, Gio, GLib  # noqa: E402

HERE = pathlib.Path(__file__).resolve().parent
MAIN = HERE / "mlqt-app-icon.svg"
SMALL = HERE / "mlqt-app-icon-16.svg"

PNG_SIZES = [24, 32, 48, 256, 512]   # from MAIN
ICO_SIZES = [16, 24, 32, 48, 256]    # what the existing mlqt.ico carries


def render(svg_path, size, out_path):
    data = GLib.Bytes.new(svg_path.read_bytes())
    stream = Gio.MemoryInputStream.new_from_bytes(data)
    pixbuf = GdkPixbuf.Pixbuf.new_from_stream_at_scale(stream, size, size, True, None)
    pixbuf.savev(str(out_path), "png", [], [])
    return out_path


def build_ico(frames):
    """Pack PNG frames into an .ico, PNG-compressed rather than as BMP.

    Windows has read PNG frames at any size since Vista, and it is what the designer's original
    file did. A frame 256px wide records its size as 0, the format's way of spelling 256 in a byte.
    """
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = len(header) + 16 * len(frames)
    entries, blobs = [], []
    for png in frames:
        width, height = struct.unpack(">II", png[16:24])
        entries.append(struct.pack("<BBBBHHII", width % 256, height % 256, 0, 0,
                                   1, 32, len(png), offset))
        blobs.append(png)
        offset += len(png)
    return b"".join([header, *entries, *blobs])


def main():
    for svg in (MAIN, SMALL):
        if not svg.is_file():
            sys.exit(f"missing source: {svg}")

    written = [render(SMALL, 16, HERE / "mlqt-16.png")]
    written += [render(MAIN, n, HERE / f"mlqt-{n}.png") for n in PNG_SIZES]

    ico = HERE / "mlqt.ico"
    ico.write_bytes(build_ico([(HERE / f"mlqt-{n}.png").read_bytes() for n in ICO_SIZES]))
    written.append(ico)

    for path in written:
        print(f"  {path.name}")


if __name__ == "__main__":
    main()
