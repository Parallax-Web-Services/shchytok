#!/usr/bin/env python3
"""Import one pinned Chromium source tree without merging upstream history.

The outer Parallax repository owns src/. Dependencies are synchronized separately
through gclient. This command never stages, commits, resets, or deletes files.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[2]


def load_pin(root=ROOT):
    return json.loads((root / "parallax/upstream.json").read_text())["chromium"]


def git(*args, cwd=ROOT, **kwargs):
    env = dict(os.environ, GIT_TERMINAL_PROMPT="0", GIT_ASKPASS="/bin/false")
    return subprocess.run(
        ["git", "-c", "credential.helper=", "-c", "core.askPass=", *args],
        cwd=cwd, env=env, check=True, **kwargs,
    )


def safe_member(member):
    """Refuse archive entries that could escape the source directory."""
    path = PurePosixPath(member.name)
    if path.is_absolute() or ".." in path.parts or ".git" in path.parts:
        raise ValueError(f"Unsafe archive path: {member.name}")
    if member.isdev() or member.isfifo() or member.islnk():
        raise ValueError(f"Unsupported archive entry: {member.name}")
    if member.issym():
        target = PurePosixPath(member.linkname)
        if target.is_absolute():
            raise ValueError(f"Absolute archive symlink: {member.name}")
        depth = len(path.parent.parts)
        for part in target.parts:
            depth += -1 if part == ".." else (0 if part == "." else 1)
            if depth < 0:
                raise ValueError(f"Escaping archive symlink: {member.name}")


def import_tree(root, pin, fetch=True):
    source = root / pin["source_directory"]
    if source.exists() and any(source.iterdir()):
        raise RuntimeError("src/ must be empty; refusing to overwrite an existing checkout.")
    revision = pin["revision"]
    if fetch:
        git("fetch", "--no-tags", "--depth=1", pin["repository"], revision, cwd=root)
    resolved = git("rev-parse", f"{revision}^{{commit}}", cwd=root,
                   capture_output=True, text=True).stdout.strip()
    if resolved != revision:
        raise RuntimeError("The fetched Chromium commit does not match upstream.json.")
    module_listing = git("ls-tree", revision, ".gitmodules", cwd=root,
                         capture_output=True, text=True).stdout
    modules = None
    if module_listing:
        upstream_modules = git("show", f"{revision}:.gitmodules", cwd=root,
                               capture_output=True, text=True).stdout
        modules = re.sub(r"(?m)^(\s*path\s*=\s*)(.+)$",
                         lambda match: match[1] + pin["source_directory"] + "/" + match[2],
                         upstream_modules)
        outer_modules = root / ".gitmodules"
        if outer_modules.exists() and outer_modules.read_text() != modules:
            raise RuntimeError("Outer .gitmodules differs; preserve and review it before importing.")
    source.mkdir(exist_ok=True)
    process = subprocess.Popen(["git", "-c", "core.autocrlf=false", "-c", "core.eol=lf",
                                "archive", "--format=tar", revision],
                               cwd=root, stdout=subprocess.PIPE)
    count = 0
    try:
        with tarfile.open(fileobj=process.stdout, mode="r|") as archive:
            for member in archive:
                safe_member(member)
                archive.extract(member, source, filter="data")
                count += int(member.isfile())
        if process.wait() != 0:
            raise RuntimeError("git archive failed; the source import is incomplete.")
    except BaseException:
        process.kill()
        process.wait()
        raise
    finally:
        process.stdout.close()
    # Chromium's SYNC mode verifies upstream gitlinks. The squashed source is
    # owned by our outer repository, so gclient must use the pinned DEPS instead.
    deps_path = source / "DEPS"
    deps = deps_path.read_text()
    original = "git_dependencies = 'SYNC'"
    if deps.count(original) != 1:
        raise RuntimeError("Unexpected DEPS layout; review the source pin before importing.")
    replacement = (
        "# Parallax: the squashed source snapshot uses DEPS instead of Chromium gitlinks.\n"
        "git_dependencies = 'DEPS'"
    )
    deps_path.write_text(deps.replace(original, replacement))
    if modules is not None:
        (root / ".gitmodules").write_text(modules)
    state = {"revision": revision, "tag": pin["tag"], "files": count,
             "deps_sha256": hashlib.sha256(deps_path.read_bytes()).hexdigest()}
    state_path = root / "parallax/.build/import-state.json"
    state_path.parent.mkdir(parents=True, exist_ok=True)
    state_path.write_text(json.dumps(state, indent=2) + "\n")
    print(f"Imported {count:,} Chromium files at {revision} into {source}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--already-fetched", action="store_true")
    args = parser.parse_args()
    if sys.version_info < (3, 12):
        parser.error("Use Python 3.12 or later for safe tar extraction and relative symlink support.")
    try:
        import_tree(ROOT, load_pin(), fetch=not args.already_fetched)
    except (OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Import failed: {error}\n")


if __name__ == "__main__":
    main()
