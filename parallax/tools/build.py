#!/usr/bin/env python3
"""Build the checked-in Parallax Chromium fork on Linux without changing fork refs."""

from __future__ import annotations

import argparse
import ast
import json
import os
from pathlib import Path
import platform
import re
import shlex
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
MIN_FREE_BYTES = 100 * 1024**3
MIN_RAM_BYTES = 16 * 1024**3
OUTPUTS = {"release": "Parallax", "development": "ParallaxDevelopment",
           "os-development": "ParallaxOS"}
DEPOT_REPOSITORY = "https://chromium.googlesource.com/chromium/tools/depot_tools.git"
LASTCHANGE_HOOK = {
    "name": "lastchange",
    "pattern": ".",
    "action": [
        "python3", "src/build/util/lastchange.py",
        "-o", "src/build/util/LASTCHANGE", "--filter", "",
    ],
}


LASTCHANGE_CROS_HOOK = {
    "name": "lastchange_commit_position_cros",
    "pattern": ".",
    "condition": "checkout_chromeos",
    "action": [
        "python3", "src/build/util/lastchange.py", "-m", "CHROMIUM",
        "--commit-position-header", "src/build/util/LASTCHANGE_commit_position.h",
        "--filter", "",
    ],
}

class BuildError(RuntimeError):
    """An actionable build prerequisite failure."""


def load_manifest(root: Path) -> dict:
    manifest = json.loads((root / "parallax/upstream.json").read_text(encoding="utf-8"))
    for name in ("chromium", "depot_tools"):
        entry = manifest[name]
        if not re.fullmatch(r"[0-9a-f]{40}", entry["revision"]):
            raise BuildError(f"{name}.revision must be a full immutable Git revision")
        if not entry["repository"].startswith("https://"):
            raise BuildError(f"{name}.repository must use HTTPS")
    if manifest["depot_tools"]["repository"] != DEPOT_REPOSITORY:
        raise BuildError("depot_tools must use the official public HTTPS repository")
    return manifest


def command_environment(depot: Path | None = None) -> dict[str, str]:
    env = os.environ.copy()
    env.update(
        DEPOT_TOOLS_UPDATE="0",
        DEPOT_TOOLS_METRICS="0",
        DEPOT_TOOLS_COLLECT_METRICS="0",
        GIT_TERMINAL_PROMPT="0",
        GCM_INTERACTIVE="never",
        GIT_ASKPASS="/bin/false",
        SSH_ASKPASS="/bin/false",
    )
    # These settings also reach Git subprocesses inside gclient.
    count = int(env.get("GIT_CONFIG_COUNT", "0"))
    settings = (
        ("credential.helper", ""),
        ("core.askPass", ""),
        ("protocol.allow", "never"),
        ("protocol.https.allow", "always"),
        ("core.autocrlf", "false"),
        ("core.eol", "lf"),
    )
    if sys.platform == "linux":
        settings += (("core.symlinks", "true"),)
    for index, (key, value) in enumerate(settings, count):
        env[f"GIT_CONFIG_KEY_{index}"] = key
        env[f"GIT_CONFIG_VALUE_{index}"] = value
    env["GIT_CONFIG_COUNT"] = str(count + len(settings))
    if depot:
        env["PATH"] = str(depot) + os.pathsep + env.get("PATH", "")
    return env


def run(args: list[str], *, cwd: Path, env: dict[str, str], capture: bool = False) -> str:
    print(f"+ {shlex.join(str(arg) for arg in args)}", flush=True)
    result = subprocess.run(args, cwd=cwd, env=env, check=True, text=True,
                            stdout=subprocess.PIPE if capture else None)
    return result.stdout.strip() if capture else ""


def git(args: list[str], *, cwd: Path, capture: bool = False) -> str:
    return run(["git", "-c", "credential.helper=", "-c", "core.askPass=", *args],
               cwd=cwd, env=command_environment(), capture=capture)


def validate_client(root: Path) -> None:
    """Inspect literal config without executing the Python .gclient file."""
    config = {}
    tree = ast.parse((root / ".gclient").read_text(encoding="utf-8"))
    for statement in tree.body:
        if not isinstance(statement, ast.Assign) or len(statement.targets) != 1:
            raise BuildError(".gclient must contain only literal configuration assignments")
        target = statement.targets[0]
        if not isinstance(target, ast.Name):
            raise BuildError("Invalid .gclient assignment")
        config[target.id] = ast.literal_eval(statement.value)
    solutions = config.get("solutions", [])
    if len(solutions) != 1:
        raise BuildError("Expected one unmanaged src solution in .gclient")
    solution = solutions[0]
    if (solution.get("name") != "src" or solution.get("url") is not None
            or solution.get("managed") is not False or solution.get("deps_file") != "DEPS"):
        raise BuildError(".gclient must use name='src', url=None, managed=False, deps_file='DEPS'")
    if config.get("target_os") != ["linux", "chromeos"] or config.get("target_os_only") is not True:
        raise BuildError("This workflow requires Linux and ChromeOS .gclient targets")
    if solution.get("custom_hooks") != [LASTCHANGE_HOOK, LASTCHANGE_CROS_HOOK]:
        raise BuildError(".gclient must preserve the scoped Parallax LASTCHANGE hook")
    custom_vars = solution.get("custom_vars", {})
    for key in ("cros_boards", "cros_boards_with_qemu_images"):
        if custom_vars.get(key):
            raise BuildError("The local Ash target must not download a ChromeOS board SDK or image")
    for key in ("checkout_src_internal", "checkout_src_internal_infra", "checkout_google_internal"):
        if custom_vars.get(key):
            raise BuildError("Parallax builds use public Chromium dependencies")
    if solution.get("custom_deps"):
        raise BuildError("Unexpected .gclient custom_deps; review the dependency configuration")


def prepare_source(root: Path, *, check: bool = False) -> None:
    """Use pinned DEPS when the source squash has no upstream Chromium gitlinks."""
    deps_path = root / "src/DEPS"
    content = deps_path.read_text(encoding="utf-8")
    if content.count("git_dependencies = 'DEPS'") == 1:
        return
    upstream_line = "git_dependencies = 'SYNC'"
    if content.count(upstream_line) != 1:
        raise BuildError("Unrecognized src/DEPS git_dependencies declaration; review this Chromium roll")
    if check:
        raise BuildError("Source needs preparation: python3 parallax/tools/build.py prepare")
    replacement = (
        "# Parallax: the squashed source snapshot uses DEPS instead of Chromium gitlinks.\n"
        "git_dependencies = 'DEPS'"
    )
    deps_path.write_text(content.replace(upstream_line, replacement), encoding="utf-8")
    print("Prepared src/DEPS for the tracked source snapshot (SYNC -> DEPS).")


def ram_bytes() -> int:
    return os.sysconf("SC_PHYS_PAGES") * os.sysconf("SC_PAGE_SIZE")


def preflight(root: Path) -> None:
    if sys.platform != "linux" or platform.machine().lower() not in ("x86_64", "amd64"):
        raise BuildError("Builds require Linux x86-64. Windows and macOS builds are not validated.")
    if sys.version_info < (3, 10):
        raise BuildError("Python 3.10 or newer is required (3.11 is recommended)")
    if any(character.isspace() for character in str(root)):
        raise BuildError("Chromium requires a checkout path without whitespace")
    if not shutil.which("git"):
        raise BuildError("Install Git before bootstrapping")
    free = shutil.disk_usage(root).free
    if free < MIN_FREE_BYTES:
        raise BuildError(f"Need at least 100 GiB free; this filesystem has {free / 1024**3:.1f} GiB")
    memory = ram_bytes()
    if memory <= MIN_RAM_BYTES:
        raise BuildError(f"Parallax builds require more than 16 GiB RAM; found {memory / 1024**3:.1f} GiB")
    for path in ("src/DEPS", "src/BUILD.gn", "src/chrome/VERSION", "src/build/install-build-deps.sh"):
        if not (root / path).is_file():
            raise BuildError(f"Missing {path}; use the complete checked-in Chromium source snapshot")
    if (root / "src/.git").exists():
        raise BuildError("src must belong to the outer Parallax repository without a nested .git")
    manifest = load_manifest(root)
    version = {}
    for line in (root / "src/chrome/VERSION").read_text(encoding="utf-8").splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            version[key] = value.strip()
    source_version = ".".join(version.get(key, "") for key in ("MAJOR", "MINOR", "BUILD", "PATCH"))
    if source_version != manifest["chromium"]["tag"]:
        raise BuildError(f"Source version {source_version} differs from upstream.json")
    validate_client(root)
    print(f"Preflight: Linux x86-64, {memory / 1024**3:.1f} GiB RAM, {free / 1024**3:.1f} GiB free.")


def depot_path(root: Path) -> Path:
    return root / "parallax/.cache/depot_tools"


def verify_depot(root: Path, manifest: dict) -> Path:
    depot = depot_path(root)
    if not (depot / ".git").is_dir():
        raise BuildError("Pinned depot_tools is absent: python3 parallax/tools/build.py bootstrap")
    actual = git(["rev-parse", "HEAD"], cwd=depot, capture=True)
    if actual != manifest["depot_tools"]["revision"]:
        raise BuildError("depot_tools revision differs from upstream.json; preserve local edits and replace the cache")
    origin = git(["remote", "get-url", "origin"], cwd=depot, capture=True)
    if origin != manifest["depot_tools"]["repository"]:
        raise BuildError("Unexpected depot_tools origin; preserve the cache and investigate")
    changes = git(["status", "--porcelain", "--untracked-files=no"], cwd=depot, capture=True)
    if changes:
        raise BuildError("depot_tools contains tracked edits; preserve them before rebuilding the tool cache")
    return depot


def bootstrap(root: Path, manifest: dict) -> None:
    depot = depot_path(root)
    if depot.exists():
        verify_depot(root, manifest)
        print("Pinned depot_tools already available.")
    else:
        depot.parent.mkdir(parents=True, exist_ok=True)
        repository = manifest["depot_tools"]["repository"]
        revision = manifest["depot_tools"]["revision"]
        git(["clone", "--no-checkout", "--depth=1", repository, str(depot)], cwd=root)
        git(["fetch", "--depth=1", "origin", revision], cwd=depot)
        # This checkout is only in the new tool cache, never the Parallax fork.
        git(["checkout", "--detach", revision], cwd=depot)
        verify_depot(root, manifest)
    # The pinned checkout still needs its hermetic Python and CIPD runtime.
    # Unlike update_depot_tools, ensure_bootstrap never advances the checkout.
    env = command_environment(depot)
    env["DEPOT_TOOLS_DIR"] = str(depot)
    env["DEPOT_TOOLS_BOOTSTRAP_PYTHON3"] = "1"
    run([str(depot / "ensure_bootstrap")], cwd=depot, env=env)
    if not (depot / "python3_bin_reldir.txt").is_file():
        raise BuildError("depot_tools Python runtime was not initialized; check ensure_bootstrap output")
    verify_depot(root, manifest)


def check_branding(root: Path) -> None:
    for script in ("generate_branding.py", "apply_branding.py"):
        run([sys.executable, str(root / "parallax/tools" / script),
             "--source-root", str(root / "src"), "--check"],
            cwd=root, env=command_environment())


def brand(root: Path) -> None:
    for script in ("generate_branding.py", "apply_branding.py"):
        run([sys.executable, str(root / "parallax/tools" / script),
             "--source-root", str(root / "src")], cwd=root, env=command_environment())
    check_branding(root)


def sync(root: Path, manifest: dict) -> None:
    depot = verify_depot(root, manifest)
    prepare_source(root)
    env = command_environment(depot)
    run([str(depot / "gclient"), "sync", "--nohooks", "--no-history"], cwd=root, env=env)
    run([str(depot / "gclient"), "runhooks"], cwd=root, env=env)


def configure(root: Path, manifest: dict, configuration: str) -> Path:
    depot = verify_depot(root, manifest)
    prepare_source(root, check=True)
    check_branding(root)
    source = root / "src"
    output = source / "out" / OUTPUTS[configuration]
    preset = (root / "parallax/config" / f"{configuration}.gn").read_text(encoding="utf-8")
    output.mkdir(parents=True, exist_ok=True)
    args_path = output / "args.gn"
    if args_path.exists() and args_path.read_text(encoding="utf-8") != preset:
        raise BuildError(f"{args_path} contains different arguments; preserve your edits before replacing this output directory")
    args_path.write_text(preset, encoding="utf-8")
    run([str(depot / "gn"), "gen", str(output.relative_to(source)), "--check"],
        cwd=source, env=command_environment(depot))
    return output


def build(root: Path, manifest: dict, configuration: str, jobs: int | None) -> None:
    output = configure(root, manifest, configuration)
    depot = verify_depot(root, manifest)
    args = [str(depot / "autoninja"), "-C", str(output.relative_to(root / "src")), "chrome"]
    if jobs is not None:
        args.extend(["-j", str(jobs)])
    run(args, cwd=root / "src", env=command_environment(depot))
    print(f"Built Parallax: {output / 'chrome'}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("doctor", "prepare", "bootstrap", "brand", "sync", "configure", "build", "all"))
    parser.add_argument("--configuration", choices=tuple(OUTPUTS), default="release")
    parser.add_argument("--jobs", type=int, help="Maximum compile parallelism")
    args = parser.parse_args(argv)
    if args.jobs is not None and args.jobs < 1:
        parser.error("--jobs must be positive")
    try:
        manifest = load_manifest(ROOT)
        # Local deterministic operations are available on other host systems too.
        if args.command == "prepare":
            validate_client(ROOT)
            prepare_source(ROOT)
            return 0
        if args.command == "brand":
            brand(ROOT)
            return 0
        preflight(ROOT)
        if args.command == "doctor":
            prepare_source(ROOT, check=True)
            verify_depot(ROOT, manifest)
            check_branding(ROOT)
        elif args.command == "bootstrap":
            bootstrap(ROOT, manifest)
        elif args.command == "sync":
            sync(ROOT, manifest)
        elif args.command == "configure":
            configure(ROOT, manifest, args.configuration)
        elif args.command == "build":
            build(ROOT, manifest, args.configuration, args.jobs)
        elif args.command == "all":
            bootstrap(ROOT, manifest)
            sync(ROOT, manifest)
            build(ROOT, manifest, args.configuration, args.jobs)
    except (BuildError, OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f"Parallax: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())