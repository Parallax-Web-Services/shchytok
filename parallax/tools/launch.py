#!/usr/bin/env python3
"""Launch a local Parallax build with its own profile and Chromium's sandbox."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[2]
OUTPUTS = {"release": "Parallax", "development": "ParallaxDevelopment"}
UNSAFE_FLAGS = {
    "--no-sandbox", "--disable-setuid-sandbox", "--disable-seccomp-filter-sandbox",
    "--disable-namespace-sandbox", "--disable-gpu-sandbox", "--single-process",
}


def launch_arguments(root: Path, configuration: str, browser_args: list[str]) -> list[str]:
    if any(argument.split("=", 1)[0] in UNSAFE_FLAGS for argument in browser_args):
        raise ValueError("Sandbox-disabling flags are unsupported by the Parallax launcher")
    binary = root / "src/out" / OUTPUTS[configuration] / "chrome"
    if not binary.is_file():
        raise ValueError(f"Build Parallax first: python3 parallax/tools/build.py build --configuration {configuration}")
    config_home = Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config")
    if not config_home.is_absolute():
        config_home = Path.home() / ".config"
    profile = config_home / ("parallax" if configuration == "release" else "parallax-development")
    # An explicit user-data-dir is permitted; defaults use a separate Parallax profile.
    explicit_profile = any(arg.split("=", 1)[0] == "--user-data-dir" for arg in browser_args)
    profile_args = [] if explicit_profile else [f"--user-data-dir={profile}"]
    return [str(binary), *profile_args, "--class=parallax", *browser_args]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", choices=tuple(OUTPUTS), default="release")
    parser.add_argument("browser_args", nargs=argparse.REMAINDER, help="Browser arguments after --")
    args = parser.parse_args(argv)
    browser_args = args.browser_args[1:] if args.browser_args[:1] == ["--"] else args.browser_args
    try:
        if sys.platform != "linux":
            raise ValueError("This launcher supports Linux builds")
        if os.geteuid() == 0:
            raise ValueError("Launch Parallax as a regular user so Chromium's sandbox remains enabled")
        command = launch_arguments(ROOT, args.configuration, browser_args)
        os.execv(command[0], command)
    except (OSError, ValueError) as error:
        print(f"Parallax: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())