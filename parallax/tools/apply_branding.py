#!/usr/bin/env python3
"""Apply the reviewed Parallax branding delta to Chromium 155.0.8059.84.

Only the exact upstream inputs, or their exact branded outputs, are accepted.
This deliberately fails on source drift instead of guessing at a new Chromium
revision. Run after source extraction, before local edits to the covered files.
All inputs are validated before staging any output; replacements are atomic per
file. --check is read-only and exits 1 when branding still needs to be applied.

Linux identity is covered. Internal chrome executable and URL scheme names,
ChromiumOS, upstream credits, and macOS/Windows installation identities remain
upstream. GRIT falls back to English for changed strings without translations.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
from typing import Callable
import xml.etree.ElementTree as ET


UPSTREAM_VERSION = "155.0.8059.84"
PRODUCT = "Parallax"
COMPANY = "Parallax Web Services"


class BrandingError(ValueError):
    """The checkout is not an accepted input to this branding revision."""


def replace_once(text: str, old: str, new: str) -> str:
    """Replace one reviewed context, refusing absent or ambiguous contexts."""
    count = text.count(old)
    if count != 1:
        raise BrandingError(f"expected one occurrence of {old!r}; found {count}")
    return text.replace(old, new, 1)


def brand_identity(text: str) -> str:
    for key, old, new in (
        ("COMPANY_FULLNAME", "The Chromium Authors", COMPANY),
        ("COMPANY_SHORTNAME", "The Chromium Authors", COMPANY),
        ("PRODUCT_FULLNAME", "Chromium", PRODUCT),
        ("PRODUCT_SHORTNAME", "Chromium", PRODUCT),
        ("PRODUCT_INSTALLER_FULLNAME", "Chromium Installer", "Parallax Installer"),
        ("PRODUCT_INSTALLER_SHORTNAME", "Chromium Installer", "Parallax Installer"),
    ):
        text = replace_once(text, f"{key}={old}\n", f"{key}={new}\n")
    return text


# Keep the original XML byte layout and therefore comments, IDs, descriptions,
# translations, placeholders, and markup attributes. Parse before and after to
# validate structure; only actual message text outside example nodes is changed.
_MESSAGE = re.compile(
    r"<!--.*?-->|(?P<open><message\b(?:[^>\"']|\"[^\"]*\"|'[^']*')*>)"
    r"(?P<body>.*?)</message>", re.DOTALL,
)
_XML_TOKEN = re.compile(r"(<!--.*?-->|<(?:[^>\"']|\"[^\"]*\"|'[^']*')*>)", re.DOTALL)
_PRODUCT_WORD = re.compile(r"\bChromium\b")


def brand_messages(text: str) -> str:
    ET.fromstring(text)

    def transform(match: re.Match[str]) -> str:
        opening, body = match.group("open"), match.group("body")
        if opening is None:
            return match.group(0)
        name_match = re.search(r"\bname\s*=\s*([\"'])(.*?)\1", opening)
        if name_match is None:
            raise BrandingError("message is missing a quoted name")
        name = name_match.group(2)
        if name == "IDS_ABOUT_VERSION_COMPANY_NAME" and "The Chromium Authors" in body:
            body = replace_once(body, "The Chromium Authors", COMPANY)
        elif ("COPYRIGHT" not in name and "LICENSE" not in name
              and not body.strip().startswith("Not used in Chromium.")):
            tokens = _XML_TOKEN.split(body)
            example_depth = 0
            for index, token in enumerate(tokens):
                if token.startswith("<"):
                    if re.match(r"<ex(?:\s|>)", token):
                        example_depth += 1
                    elif re.match(r"</ex\s*>", token):
                        example_depth -= 1
                elif not example_depth:
                    tokens[index] = _PRODUCT_WORD.sub(PRODUCT, token)
            body = "".join(tokens)
        return opening + body + "</message>"

    result = _MESSAGE.sub(transform, text)
    ET.fromstring(result)
    return result


def brand_linux_profile(text: str) -> str:
    return replace_once(
        text,
        '#else\n  std::string data_dir_basename = "chromium";\n#endif',
        '#else\n  std::string data_dir_basename = "parallax";\n#endif',
    )


def brand_linux_desktop(text: str) -> str:
    return replace_once(
        text,
        '  return "chromium-browser.desktop";\n#endif\n}',
        '  return "parallax.desktop";\n#endif\n}',
    )


def brand_linux_icon(text: str) -> str:
    # WM_CLASS already derives from GetDesktopName() in this upstream version.
    # The later GetDirectLaunchUrlScheme() is intentionally left untouched.
    return replace_once(
        text,
        '#else  // BUILDFLAG(CHROMIUM_BRANDING)\n'
        '  return "chromium-browser";\n#endif\n}',
        '#else  // BUILDFLAG(CHROMIUM_BRANDING)\n'
        '  return "parallax";\n#endif\n}',
    )


@dataclass(frozen=True)
class Input:
    path: str
    upstream_sha256: str
    branded_sha256: str
    transform: Callable[[str], str]


# Hashes of bytes from the official immutable 155.0.8059.84 source tag, obtained
# via https://chromium.googlesource.com/chromium/src/+/refs/tags/155.0.8059.84/ .
# Updating Chromium requires reviewing these transformations and both hashes.
INPUTS = (
    Input("chrome/app/theme/chromium/BRANDING",
          "15ac20cb8744ebac0815961059231baefa71fd4ed3023cb2094aaf8a34ae423b",
          "d3cbfc28494d83d9ce667e9859ca28c95aa80f35161f8c83f9bcf24f46740446", brand_identity),
    Input("chrome/app/chromium_strings.grd",
          "848216b77ae1c57f8bce5f2fdede59ead363cdabb618c0998ae841c5a2574c8a",
          "36ec04daa9d035a639d1574a03f9d89f775c054ef8062d98a00b7cde87e826c3", brand_messages),
    Input("chrome/app/settings_chromium_strings.grdp",
          "9fb3a5d1020f25121dbc59df33031554bd9f8498c74aa5c220a26364ee0d2b57",
          "eba3841353ca89da8e27949c6a72940e27ff09fad95e0e1a8c9165b86ea65c05", brand_messages),
    Input("chrome/common/chrome_paths_linux.cc",
          "e4d5a3c62d12f018b5e37dc86a474570ae278b36f772863802f1663e923d33cc",
          "b0e48d0e74cd10b90348be45464bb11f85f49fc91f99eceb59683c1732af6b69", brand_linux_profile),
    Input("chrome/common/channel_info_posix.cc",
          "916bbcb36b9d6d07631cac1b69e0ac1aa3009793ae0932417f6c17cd85e85a97",
          "452199ce879c701299d1788d646b4c06486dc2a8655700980a9d2197eee210fe", brand_linux_desktop),
    Input("chrome/browser/shell_integration_linux.cc",
          "b4509b121e301eff8889a038ae6c917fa228877099b248273d7220043c318779",
          "67eac12997e3539128f4069a6b1dd2dadd64721e69771140f29e493a471fea37", brand_linux_icon),
)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


@dataclass(frozen=True)
class Change:
    path: Path
    before: bytes
    after: bytes
    mode: int


def preflight(source_root: Path, inputs: tuple[Input, ...] | None = None) -> list[Change]:
    """Read and validate every covered file, returning the complete write plan."""
    root = source_root.resolve(strict=True)
    if inputs is None:
        inputs = INPUTS
    changes = []
    for entry in inputs:
        path = root / entry.path
        if path.is_symlink() or not path.is_file():
            raise BrandingError(f"required regular source file missing: {entry.path}")
        if root not in path.resolve().parents:
            raise BrandingError(f"source file escapes the checkout: {entry.path}")
        before = path.read_bytes()
        current = digest(before)
        if current == entry.branded_sha256:
            continue
        if current != entry.upstream_sha256:
            raise BrandingError(
                f"source drift in {entry.path}: SHA-256 {current}; expected "
                f"Chromium {UPSTREAM_VERSION} or the reviewed Parallax output"
            )
        after = entry.transform(before.decode("utf-8")).encode("utf-8")
        if digest(after) != entry.branded_sha256:
            raise BrandingError(f"unexpected branding output for {entry.path}")
        changes.append(Change(path, before, after, stat.S_IMODE(path.stat().st_mode)))
    return changes


def _stage(path: Path, data: bytes, mode: int) -> Path:
    fd, filename = tempfile.mkstemp(prefix=f".{path.name}.parallax-", dir=path.parent)
    temporary = Path(filename)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        temporary.chmod(mode)
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise
    return temporary


def apply_changes(changes: list[Change]) -> None:
    """Stage all files, recheck concurrent edits, then atomically replace each."""
    staged: list[tuple[Change, Path]] = []
    replaced: list[Change] = []
    try:
        for change in changes:
            staged.append((change, _stage(change.path, change.after, change.mode)))
        for change, _ in staged:
            if change.path.read_bytes() != change.before:
                raise BrandingError(f"source changed after preflight: {change.path}")
        try:
            for change, temporary in staged:
                os.replace(temporary, change.path)
                replaced.append(change)
        except OSError:
            # This is a multi-file operation, not a filesystem transaction.
            # Restore files already replaced if a later replacement fails.
            for change in reversed(replaced):
                rollback = _stage(change.path, change.before, change.mode)
                try:
                    os.replace(rollback, change.path)
                finally:
                    rollback.unlink(missing_ok=True)
            raise
    finally:
        for _, temporary in staged:
            temporary.unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", "--root", type=Path,
                        default=Path(__file__).resolve().parents[2] / "src")
    parser.add_argument("--check", action="store_true",
                        help="verify branding without writing (exit 1 if unapplied)")
    args = parser.parse_args(argv)
    try:
        changes = preflight(args.source_root)
        if args.check:
            if changes:
                print("Branding is required for:")
                for change in changes:
                    print(f"  {change.path.relative_to(args.source_root.resolve())}")
                return 1
            print(f"Parallax branding verified (Chromium {UPSTREAM_VERSION}).")
        else:
            apply_changes(changes)
            print(f"Parallax branding applied: {len(changes)} file(s) changed.")
        return 0
    except (OSError, UnicodeError, ET.ParseError, BrandingError) as exc:
        print(f"Branding failed: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
