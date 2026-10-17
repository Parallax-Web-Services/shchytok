#!/usr/bin/env python3
"""Verify Parallax identities and original artwork in generated Chromium resources.

Run after the focused resource targets documented in parallax/README.md. This
reads build artifacts and Chromium's own GRIT pack reader; it installs nothing,
does not regenerate resources, and suppresses Python bytecode writes.
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import importlib
from pathlib import Path
import re
import sys
import zlib


ROOT = Path(__file__).resolve().parents[2]
LOGO = ROOT / "parallax/branding/parallax-orbital.svg"
LOGO_SHA256 = "fb6317a9dda1267265ad18147173aa60ee14696a18b5dde3221973de8dbb13a5"
COPYRIGHT = "Copyright {0,date,y} The Chromium Authors. All rights reserved."


class VerificationError(ValueError):
    """An artifact is absent, invalid, or does not contain Parallax branding."""


def definition(header: str, name: str) -> str:
    matches = re.findall(r"^\s*#define\s+" + re.escape(name) + r"[ \t]+([^\r\n]+)",
                         header, re.MULTILINE)
    if len(matches) != 1:
        raise VerificationError(f"expected one generated definition for {name}; found {len(matches)}")
    return matches[0].strip()


def resource_id(header: str, name: str) -> int:
    value = definition(header, name)
    if value.isdecimal():
        return int(value)
    # Official/non-component configurations can enable GRIT allowlist macros.
    allowlist = re.fullmatch(r"\(::ui::AllowlistedResource<(\d+)>\(\),\s*\1\)", value)
    if allowlist:
        return int(allowlist[1])
    raise VerificationError(f"unsupported resource ID definition for {name}: {value}")


def resource_bytes(pack, header: str, name: str) -> bytes:
    identifier = resource_id(header, name)
    if identifier not in pack.resources:
        raise VerificationError(f"{name} (ID {identifier}) is missing from the resource pack")
    data = pack.resources[identifier]
    # SVG includes use GRIT's default gzip compression; PNG includes stay raw.
    if data.startswith(b"\x1f\x8b"):
        try:
            return gzip.decompress(data)
        except (OSError, EOFError, zlib.error) as error:
            raise VerificationError(f"invalid gzip resource {name}: {error}") from error
    return data


def resource_text(pack, header: str, name: str, reader) -> str:
    encodings = {reader.UTF8: "utf-8", reader.UTF16: "utf-16-le"}
    if pack.encoding not in encodings:
        raise VerificationError(f"unsupported string-pack encoding: {pack.encoding}")
    try:
        return resource_bytes(pack, header, name).decode(encodings[pack.encoding])
    except UnicodeError as error:
        raise VerificationError(f"invalid encoded string for {name}: {error}") from error


def verify_version_header(header: str) -> None:
    for name, expected in (
        ("PRODUCT_FULLNAME_STRING", '"Parallax"'),
        ("PRODUCT_SHORTNAME_STRING", '"Parallax"'),
        ("COMPANY_FULLNAME_STRING", '"Parallax Web Services"'),
        ("COMPANY_SHORTNAME_STRING", '"Parallax Web Services"'),
    ):
        actual = definition(header, name)
        if actual != expected:
            raise VerificationError(f"{name}: expected {expected}, found {actual}")
    # Upstream version.py uses LASTCHANGE.dummy for non-official builds. That
    # file has no LASTCHANGE_YEAR, so the original BRANDING placeholder remains
    # literal in chrome_version.h. Official metadata can resolve it to a year.
    if not re.fullmatch(
        r'"Copyright (?:@LASTCHANGE_YEAR@|\d{4}) The Chromium Authors\. All rights reserved\."',
        definition(header, "COPYRIGHT_STRING"),
    ):
        raise VerificationError("generated version header does not preserve Chromium's copyright")


def verify(source_root: Path, output_dir: Path) -> None:
    source_root, output_dir = source_root.resolve(), output_dir.resolve()
    generated = output_dir / "gen/chrome"
    files = {
        "strings_header": generated / "grit/branded_strings.h",
        "strings_pack": generated / "branded_strings_en-US.pak",
        "images_header": generated / "grit/chrome_unscaled_resources.h",
        "images_pack": generated / "chrome_unscaled_resources.pak",
        "version_header": generated / "common/chrome_version.h",
        "linux_icon": source_root / "chrome/app/theme/chromium/linux/product_logo_64.png",
        "logo": LOGO,
    }
    missing = [str(path) for path in files.values() if not path.is_file()]
    if missing:
        raise VerificationError("missing required files (build the focused resource targets):\n  "
                                + "\n  ".join(missing))
    logo = files["logo"].read_bytes()
    if hashlib.sha256(logo).hexdigest() != LOGO_SHA256:
        raise VerificationError("the canonical orbital SVG differs from the original marketing asset")

    grit_root = source_root / "tools/grit"
    if not (grit_root / "grit/format/data_pack.py").is_file():
        raise VerificationError(f"missing Chromium GRIT resource-pack reader: {grit_root}")
    old_bytecode = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(grit_root))
    try:
        try:
            reader = importlib.import_module("grit.format.data_pack")
        except ImportError as error:
            raise VerificationError(f"could not import Chromium's GRIT reader: {error}") from error
        if not Path(reader.__file__).resolve().is_relative_to(grit_root):
            raise VerificationError("a different GRIT installation is loaded; run this verifier in a fresh Python process")

        def read_pack(path: Path):
            try:
                return reader.ReadDataPack(str(path))
            except Exception as error:
                raise VerificationError(f"cannot read resource pack {path}: {error}") from error

        strings = read_pack(files["strings_pack"])
        strings_header = files["strings_header"].read_text(encoding="utf-8")
        for name, expected in (
            ("IDS_PRODUCT_NAME", "Parallax"),
            ("IDS_SHORT_PRODUCT_NAME", "Parallax"),
            ("IDS_ABOUT_VERSION_COMPANY_NAME", "Parallax Web Services"),
            ("IDS_ABOUT_VERSION_COPYRIGHT", COPYRIGHT),
        ):
            actual = resource_text(strings, strings_header, name, reader)
            if actual != expected:
                raise VerificationError(f"{name}: expected {expected!r}, found {actual!r}")

        images = read_pack(files["images_pack"])
        images_header = files["images_header"].read_text(encoding="utf-8")
        for name, expected in (
            ("IDR_PRODUCT_LOGO_SVG", logo),
            ("IDR_PRODUCT_LOGO_ANIMATION_SVG", logo),
            ("IDR_PRODUCT_LOGO_64", files["linux_icon"].read_bytes()),
        ):
            if resource_bytes(images, images_header, name) != expected:
                raise VerificationError(f"{name} differs from the checked-in Parallax artwork")

        verify_version_header(files["version_header"].read_text(encoding="utf-8"))
    finally:
        sys.path.pop(0)
        sys.dont_write_bytecode = old_bytecode


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT / "src")
    parser.add_argument("--output-dir", type=Path,
                        help="GN output directory (default: SOURCE_ROOT/out/Parallax)")
    args = parser.parse_args(argv)
    output = args.output_dir or args.source_root / "out/Parallax"
    try:
        verify(args.source_root, output)
    except (OSError, UnicodeError, VerificationError) as error:
        print(f"Resource verification failed: {error}", file=sys.stderr)
        return 1
    print("PASS: compiled Parallax product names, company, copyrights, orbital SVGs, and Linux icon.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
