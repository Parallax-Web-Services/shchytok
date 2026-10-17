#!/usr/bin/env python3
"""Reproduce Chromium's product icons from the original Parallax orbital SVG.

Install parallax/branding/requirements.txt with Python 3.10+ and native Cairo.
Only existing unbranded product assets are replaced. --check writes nothing.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path


PARALLAX_ROOT = Path(__file__).resolve().parents[1]
LOGO = PARALLAX_ROOT / "branding" / "parallax-orbital.svg"
LOGO_SHA256 = "fb6317a9dda1267265ad18147173aa60ee14696a18b5dde3221973de8dbb13a5"
THEME_ROOTS = (
    Path("chrome/app/theme/chromium"),
    Path("chrome/app/theme/default_100_percent/chromium"),
    Path("chrome/app/theme/default_200_percent/chromium"),
)
ICO_NAMES = {"app_list.ico", "chromium.ico", "chromium_doc.ico", "chromium_pdf.ico"}
ICNS_SIZES = (32, 64, 128, 256, 512, 1024)


@dataclass(frozen=True)
class Asset:
    path: Path
    kind: str
    sizes: tuple[tuple[int, int], ...] = ()


def discover(source_root: Path, image_module) -> list[Asset]:
    """Read dimensions from the pinned checkout, including scaled/wide logos."""
    assets: dict[Path, Asset] = {}

    def register(path: Path, kind: str) -> None:
        if not path.is_file():
            return
        if not path.resolve().is_relative_to(source_root):
            raise ValueError(f"Asset escapes source root: {path}")
        if kind == "png":
            with image_module.open(path) as original:
                sizes = (original.size,)
        elif kind == "ico":
            with image_module.open(path) as original:
                sizes = tuple(sorted(original.ico.sizes()))
            if not sizes or any(w != h or not 1 <= w <= 256 for w, h in sizes):
                raise ValueError(f"Unsupported ICO frame dimensions: {path}")
        elif kind == "xpm":
            header = re.search(r'"(\d+)\s+(\d+)\s+\d+\s+\d+', path.read_text())
            if header is None:
                raise ValueError(f"Cannot read XPM dimensions: {path}")
            sizes = ((int(header[1]), int(header[2])),)
        else:
            sizes = ()
        if any(w < 1 or h < 1 or w > 4096 or h > 4096 for w, h in sizes):
            raise ValueError(f"Unsupported product icon dimensions: {path}")
        assets[path] = Asset(path, kind, sizes)

    for relative in THEME_ROOTS:
        directory = source_root / relative
        if not directory.is_dir():
            raise ValueError(f"Missing Chromium unbranded theme directory: {relative}")
        for path in sorted(directory.rglob("product_logo*.png")):
            register(path, "png")

    theme = source_root / THEME_ROOTS[0]
    for path in sorted(theme.glob("product_logo*.svg")):
        register(path, "svg")
    for path in sorted((theme / "linux").glob("product_logo*.xpm")):
        register(path, "xpm")
    for name in sorted(ICO_NAMES):
        register(theme / "win" / name, "ico")
    for path in sorted((theme / "win" / "tiles").glob("*Logo.png")):
        register(path, "png")
    register(theme / "mac" / "app.icns", "icns")
    for path in sorted((theme / "mac" / "Assets.xcassets").rglob("*.png")):
        register(path, "png")
    for path in sorted((theme / "chromeos").glob("chrome_app_icon_*.png")):
        register(path, "png")

    if not any(asset.kind == "png" for asset in assets.values()):
        raise ValueError("No Chromium product PNGs found; use --source-root with the src checkout")
    return [assets[path] for path in sorted(assets)]


class Renderer:
    def __init__(self, svg: bytes, image_module, cairo_module):
        self.svg = svg
        self.image = image_module
        self.cairo = cairo_module
        self.cache = {}

    def raster(self, size: tuple[int, int]):
        """Let SVG preserveAspectRatio center the complete mark on a clear canvas."""
        if size not in self.cache:
            width, height = size
            # Supersampling keeps the thin orbital paths visible in small icons.
            data = self.cairo.svg2png(
                bytestring=self.svg,
                output_width=width * 4,
                output_height=height * 4,
            )
            with self.image.open(io.BytesIO(data)) as rendered:
                pixels = rendered.convert("RGBA").resize(size, self.image.Resampling.LANCZOS)
            if pixels.getchannel("A").getbbox() is None:
                raise ValueError(f"SVG rendered as an empty image at {size}")
            self.cache[size] = pixels
        return self.cache[size].copy()

    def encode(self, asset: Asset) -> bytes:
        if asset.kind == "svg":
            return self.svg
        output = io.BytesIO()
        if asset.kind == "png":
            self.raster(asset.sizes[0]).save(output, "PNG", optimize=False, compress_level=9)
        elif asset.kind == "ico":
            frames = [self.raster(size) for size in asset.sizes]
            frames[-1].save(output, "ICO", sizes=asset.sizes, append_images=frames[:-1])
        elif asset.kind == "icns":
            frames = [self.raster((side, side)) for side in ICNS_SIZES]
            frames[-1].save(output, "ICNS", append_images=frames[:-1])
        elif asset.kind == "xpm":
            return self.encode_xpm(self.raster(asset.sizes[0]))
        else:
            raise ValueError(f"Unsupported output kind: {asset.kind}")
        return output.getvalue()

    def encode_xpm(self, image) -> bytes:
        """XPM supports binary transparency; quantize colors deterministically."""
        alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+"
        palette_image = image.convert("RGB").quantize(
            colors=len(alphabet), method=self.image.Quantize.MEDIANCUT, dither=self.image.Dither.NONE
        )
        alpha = list(image.getchannel("A").get_flattened_data())
        indices = list(palette_image.get_flattened_data())
        used = sorted({value for value, opacity in zip(indices, alpha) if opacity >= 128})
        codes = {index: alphabet[n] for n, index in enumerate(used)}
        palette = palette_image.getpalette()
        lines = ["/* XPM */", "/* Generated from the original Parallax orbital SVG. */",
                 "static const char * const parallax_xpm[] = {",
                 f'"{image.width} {image.height} {len(used) + 1} 1",', '". c None",']
        for index in used:
            red, green, blue = palette[index * 3:index * 3 + 3]
            lines.append(f'"{codes[index]} c #{red:02x}{green:02x}{blue:02x}",')
        for y in range(image.height):
            offset = y * image.width
            row = "".join(codes[indices[i]] if alpha[i] >= 128 else "."
                          for i in range(offset, offset + image.width))
            lines.append(f'"{row}"' + ("," if y < image.height - 1 else ""))
        lines.append("};")
        return ("\n".join(lines) + "\n").encode("ascii")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True,
                        help="Chromium src directory (for this fork: ./src)")
    parser.add_argument("--check", action="store_true",
                        help="Compare every discovered asset with a fresh render; write nothing")
    args = parser.parse_args(argv)
    try:
        from PIL import Image
        import cairosvg
    except (ImportError, OSError) as error:
        parser.exit(2, "Branding dependencies unavailable. Use Python 3.10+, install "
                    "parallax/branding/requirements.txt and native Cairo.\n"
                    f"{error}\n")

    try:
        svg = LOGO.read_bytes()
        if hashlib.sha256(svg).hexdigest() != LOGO_SHA256:
            raise ValueError(f"Canonical logo changed: {LOGO}; restore the exact marketing SVG")
        document = ET.fromstring(svg)
        if document.tag != "{http://www.w3.org/2000/svg}svg":
            raise ValueError("Canonical logo is not an SVG document")
        source_root = args.source_root.resolve(strict=True)
        assets = discover(source_root, Image)
        renderer = Renderer(svg, Image, cairosvg)
        changed = []
        # Generate everything before writing so a render failure cannot partly brand a checkout.
        expected = [(asset, renderer.encode(asset)) for asset in assets]
        for asset, data in expected:
            if asset.path.read_bytes() != data:
                changed.append(asset.path.relative_to(source_root).as_posix())
                if not args.check:
                    asset.path.write_bytes(data)
        for path in changed:
            print(("MISMATCH " if args.check else "UPDATED ") + path)
        if args.check and changed:
            print(f"Branding check failed: {len(changed)} of {len(assets)} assets differ.", file=sys.stderr)
            return 1
        print(f"{'Checked' if args.check else 'Generated'} {len(assets)} Parallax assets; "
              f"{len(changed)} {'mismatches' if args.check else 'updated'}.")
        return 0
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"Branding failed: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
