#!/usr/bin/env python3
"""Stage artwork-only branding for the pinned ChromiumOS reference guest.

Reads an already mounted reference root, never writes to it. Resource IDs are
preserved and images are identified by their actual upstream artwork, not by
assuming resource IDs match another Chromium build. This is a branding preview,
not a replacement for building the Parallax ChromiumOS distribution.
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = "6459df2678"


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def stage(reference: Path, output: Path) -> dict:
    from PIL import Image, ImageDraw, ImageFont
    import cairosvg
    from generate_branding import LOGO, LOGO_SHA256, Renderer

    sys.dont_write_bytecode = True
    sys.path.insert(0, str(ROOT / "src/tools/grit"))
    from grit.format import data_pack

    if output.exists():
        raise ValueError("Use a new output directory; existing output is preserved")
    release = (reference / "etc/lsb-release").read_text()
    if "CHROMEOS_RELEASE_BUILDER_PATH=amd64-generic-vm-public/R155-16820.30.0\n" not in release:
        raise ValueError("Reference must be the pinned R155-16820.30.0 VM image")
    svg = LOGO.read_bytes()
    if digest(svg) != LOGO_SHA256:
        raise ValueError("Canonical Parallax logo hash mismatch")
    renderer = Renderer(svg, Image, cairosvg)

    def pixels(value: bytes):
        with Image.open(io.BytesIO(value)) as image:
            return image.size, digest(image.convert("RGBA").tobytes())

    paths = subprocess.check_output([
        "git", "-C", str(ROOT), "ls-tree", "-r", "--name-only", UPSTREAM,
        "src/chrome/app/theme/chromium", "src/chrome/app/theme/default_100_percent/chromium",
        "src/chrome/app/theme/default_200_percent/chromium",
    ], text=True).splitlines()
    raster_matches = set()
    vector_matches = set()
    for path in paths:
        name = Path(path).name
        if not (name.startswith("product_logo") or name.startswith("chrome_app_icon_")):
            continue
        if not path.endswith((".png", ".svg")):
            continue
        original = subprocess.check_output(["git", "-C", str(ROOT), "show", f"{UPSTREAM}:{path}"])
        if path.endswith(".png"):
            raster_matches.add(pixels(original))
        else:
            vector_matches.add(digest(original))

    staged = {}
    report = {"kind": "parallax-station-branding-preview", "reference": "R155-16820.30.0",
              "logo_sha256": LOGO_SHA256, "packs": [], "boot_frames": [],
              "limits": "Upstream test OS and browser binary with replaced artwork; native Station build remains separate."}
    chrome = Path("opt/google/chrome")
    for name in ("resources.pak", "chrome_100_percent.pak", "chrome_200_percent.pak"):
        relative = chrome / name
        before = (reference / relative).read_bytes()
        pack = data_pack.ReadDataPackFromString(before)
        updated = dict(pack.resources)
        replacements = []
        for identifier, value in pack.resources.items():
            compressed = value.startswith(b"\x1f\x8b")
            plain = gzip.decompress(value) if compressed else value
            replacement = None
            if plain.startswith(b"\x89PNG\r\n\x1a\n"):
                key = pixels(plain)
                if key in raster_matches:
                    rendered = io.BytesIO()
                    renderer.raster(key[0]).save(rendered, "PNG")
                    replacement = rendered.getvalue()
            elif digest(plain) in vector_matches:
                replacement = svg
            if replacement is not None:
                updated[identifier] = gzip.compress(replacement, mtime=0) if compressed else replacement
                replacements.append(identifier)
        if not replacements:
            raise ValueError(f"No verified upstream product artwork matched {relative}")
        after = data_pack.WriteDataPackToString(updated, pack.encoding)
        checked = data_pack.ReadDataPackFromString(after)
        if checked.resources != updated or checked.encoding != pack.encoding:
            raise ValueError(f"Pack round-trip changed resources: {relative}")
        assert set(checked.resources) == set(pack.resources)
        assert all(checked.resources[k] == v for k, v in pack.resources.items() if k not in replacements)
        staged[relative] = after
        report["packs"].append({"path": str(relative), "ids": replacements,
                                "before_sha256": digest(before), "after_sha256": digest(after)})

    total = sum(len(p["ids"]) for p in report["packs"])
    if total < 5:
        raise ValueError("Too few verified logo replacements")

    # Keep each existing frame's dimensions and repeat static canonical artwork.
    # This replaces the boot wordmark animation without inventing a new logo.
    assets = Path("usr/share/chromeos-assets")
    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
    for scale in (100, 200):
        frames = sorted((reference / assets / f"images_{scale}_percent").glob("boot_splash_frame*.png"))
        if len(frames) != 13:
            raise ValueError(f"Expected 13 pinned boot splash frames at {scale}%")
        for frame in frames:
            relative = frame.relative_to(reference)
            with Image.open(frame) as image:
                width, height = image.size
            canvas = Image.new("RGBA", (width, height), (254, 254, 254, 255))
            side = int(height * 0.9)
            canvas.alpha_composite(renderer.raster((side, side)), (0, (height - side) // 2))
            draw = ImageDraw.Draw(canvas)
            font = ImageFont.truetype(font_path, max(12, int(height * 0.33)))
            small = ImageFont.truetype(font_path, max(10, int(height * 0.20)))
            x = side + int(height * 0.12)
            draw.text((x, height * 0.10), "Parallax", font=font, fill="#35105c")
            draw.text((x, height * 0.55), "Station", font=small, fill="#35105c")
            encoded = io.BytesIO()
            canvas.convert("RGB").save(encoded, "PNG")
            staged[relative] = encoded.getvalue()
            report["boot_frames"].append({"path": str(relative), "size": [width, height],
                                           "before_sha256": digest(frame.read_bytes()),
                                           "after_sha256": digest(encoded.getvalue())})

    # Preserve upstream release metadata and identify this artifact separately.
    staged[Path("etc/parallax-station-preview.json")] = (json.dumps({
        "kind": report["kind"], "reference": report["reference"], "logo_sha256": LOGO_SHA256,
        "description": report["limits"],
    }, indent=2) + "\n").encode()
    output.mkdir(parents=True)
    for relative, value in staged.items():
        destination = output / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(value)
    (output / "branding-report.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = stage(args.reference_root.resolve(), args.output.resolve())
    print(json.dumps({"output": str(args.output), "logo_resources": sum(len(p["ids"]) for p in report["packs"]),
                      "boot_frames": len(report["boot_frames"])}, indent=2))


if __name__ == "__main__":
    main()
