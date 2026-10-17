# Parallax browser

Parallax is a full Chromium source fork. The Chromium snapshot lives in `src/`; Parallax branding, build presets and maintenance tools live in `parallax/`. The browser is compiled from Chromium's `chrome` target.

The source is pinned to Chromium **155.0.8059.84**, revision `5869fe313571d75152b77813cf0a4ee26a4e6df0`. The exact depot_tools revision and original orbital logo checksum are recorded in [upstream.json](upstream.json). The logo is copied from the existing marketing repository's `public/assets/Parallax Mark.svg`, preserving its SVG paths.

## Build on Linux

The first build workflow targets **Linux x86-64**. Use a Linux checkout path without spaces, Git, Python **3.10 or newer** (3.11+ recommended), **more than 16 GiB RAM**, and **at least 100 GiB free disk space**. Allow additional room for dependencies, debug outputs and future upgrades. A full build can take hours. Windows and macOS compilation and packaging have not been validated.

Run these commands from the outer repository root. The Linux system dependency installer may request administrator access; the Parallax scripts never install system packages automatically.

```sh
./src/build/install-build-deps.sh
python3 -m venv parallax/.cache/venv
. parallax/.cache/venv/bin/activate
python3 -m pip install -r parallax/branding/requirements.txt
python3 parallax/tools/build.py bootstrap
python3 parallax/tools/build.py sync
python3 parallax/tools/build.py brand
python3 parallax/tools/build.py build
```

Brand generation also requires the native Cairo library, which Ubuntu provides in `libcairo2`. Install `python3-venv` if the virtual environment command is unavailable.

The workflow performs the following steps:

- `bootstrap` downloads the official public depot_tools repository at the immutable revision in `upstream.json`. Its cache lives in `parallax/.cache/depot_tools`. It also runs the official `ensure_bootstrap` helper to initialize depot_tools Python and CIPD runtimes without updating the pinned checkout. Re-running `bootstrap` repairs a missing runtime in an existing verified cache. Automatic depot_tools updates and interactive Git credential prompts are disabled. Git subprocesses use LF line endings and preserve Linux symlinks, including when host Git defaults request CRLF conversion.
- `sync` reads the pinned `src/DEPS`, downloads dependencies and runs Chromium's hooks. It can download substantial data.
- `brand` generates product icons from the original orbital SVG and applies guarded Parallax source changes. Commit these source and branding changes together.
- `build` verifies branding, writes the selected GN preset to `args.gn`, runs `gn gen --check`, then `autoninja -C out/Parallax chrome`. It preserves existing output arguments if they differ from the preset and asks you to review them.

To check prerequisites, configure without compiling, or build the development preset:

```sh
python3 parallax/tools/build.py doctor
python3 parallax/tools/build.py configure
python3 parallax/tools/build.py build --configuration development --jobs 16
```

Release output is `src/out/Parallax/chrome`. Development output is `src/out/ParallaxDevelopment/chrome`. Both presets use local compilation with Siso through `autoninja`; no remote build account is required. The release preset uses optimization with limited symbols and disables PGO profile consumption. It is an initial local build preset, not an official Chrome distribution configuration.

Once branding and system dependencies are ready, `python3 parallax/tools/build.py all` combines bootstrap, dependency sync and build. It validates checked-in branding and does not regenerate it automatically.

## Launch

Run as a regular desktop user:

```sh
python3 parallax/tools/launch.py -- https://example.com
python3 parallax/tools/launch.py --configuration development -- about:blank
```

The launcher starts the compiled browser with Chromium's sandbox enabled. It uses `$XDG_CONFIG_HOME/parallax` (normally `~/.config/parallax`) or `parallax-development` as a separate profile. It rejects sandbox-disabling command-line flags and root execution. Additional Chromium arguments go after `--`; an explicit `--user-data-dir` overrides the default profile.

For a local Linux desktop entry, keep this checkout at its current path and install the wrapper, SVG and desktop file:

```sh
mkdir -p "$HOME/.local/bin"
printf '#!/bin/sh\nexec python3 "%s/parallax/tools/launch.py" -- "$@"\n' "$PWD" > "$HOME/.local/bin/parallax"
chmod +x "$HOME/.local/bin/parallax"
install -Dm644 parallax/parallax.desktop "$HOME/.local/share/applications/parallax.desktop"
install -Dm644 parallax/branding/parallax-orbital.svg "$HOME/.local/share/icons/hicolor/scalable/apps/parallax.svg"
```

Ensure `~/.local/bin` is on your desktop session's `PATH`. The desktop launcher expects the release build.

## Source ownership and dependency sync

The outer Parallax repository owns the complete source snapshot under `src/`. There is no nested source repository or circular symlink. The root `.gclient` uses solution name `src`, `managed=False` and `url=None`: gclient reads DEPS and synchronizes dependencies while leaving the outer repository's source branch, commits and origin alone.

The outer `.gitmodules` prefixes dependency paths with `src/` so the imported Chromium gitlinks remain valid. Populate dependencies with `python3 parallax/tools/build.py sync`, which uses `gclient sync` and applies Chromium's platform and checkout conditions, including excluding private dependencies. Do not use `git submodule update` for this setup.

The named `lastchange` hook retains Chromium's output path and passes an empty commit filter, so `LASTCHANGE.committime` comes from the outer Parallax commit. This avoids Chromium's default `Change-Id` filter producing a zero revision and a 1970 build timestamp in a squashed fork. Dependency-specific Skia and Dawn revision hooks keep their upstream behavior. Commit the imported source before running dependency hooks so build metadata describes that source snapshot.

This layout matches the `src/` paths in Chromium's dependencies and hooks. A solution named `.` would place those dependencies incorrectly. The imported `src/DEPS` uses `git_dependencies = 'DEPS'` so the immutable DEPS pins are authoritative for the squashed source. The `prepare` command applies only that known, guarded upstream declaration change when needed.

```sh
python3 parallax/tools/build.py prepare
```

Do not run `fetch chromium`, `git rebase-update` or a gclient source revision override in this checkout. Source updates belong in a reviewed Chromium import with a matching manifest, dependency sync, branding validation and rebuild.

The import utility in `parallax/tools/import_upstream.py` reproduces the pinned snapshot in an empty `src/` directory. It verifies the pinned upstream revision and refuses an existing nonempty source directory. It requires Python 3.12 or newer for safe tar extraction and symlink handling. Review and preserve local source work before attempting a new import.

## Branding and verification

`is_chrome_branded = false` stays in both presets. Parallax uses the open Chromium resource path with the product text and icons patched in place. Google Chrome trademarks, private build infrastructure, Google API credentials, proprietary codecs and an automatic browser updater are not supplied.

```sh
python3 parallax/tools/generate_branding.py --source-root src --check
python3 parallax/tools/apply_branding.py --source-root src --check
python3 -m unittest discover -s parallax/tests -v
```

The helper tests cover dependency configuration, guarded source preparation, local build command sequencing, argument preservation and sandboxed launch behavior. They do not replace compiling Chromium or running its browser tests.

After dependency sync and `configure`, build the focused resource targets to validate branded strings and icons without compiling the complete browser. From the outer repository root:

```sh
export DEPOT_TOOLS_UPDATE=0
export PATH="$PWD/parallax/.cache/depot_tools:$PATH"
cd src
autoninja -C out/Parallax chrome/app:branded_strings_grit \
  chrome/app/theme:chrome_unscaled_resources_grit \
  chrome/app/theme:theme_resources_grit chrome/common:version_header
cd ..
python3 parallax/tools/verify_resources.py --source-root src --output-dir src/out/Parallax
```

The read-only verifier checks the compiled English product names and company, retained Chromium copyright, generated version identity, and packed orbital SVGs and Linux icon against the checked-in artwork. It uses Chromium's own resource-pack reader and needs no additional Python packages. These resource checks do not establish that the full browser compiles or launches.

Upstream references: [Linux build instructions](https://chromium.googlesource.com/chromium/src/+/refs/tags/155.0.8059.84/docs/linux/build_instructions.md), [pinned DEPS](https://chromium.googlesource.com/chromium/src/+/5869fe313571d75152b77813cf0a4ee26a4e6df0/DEPS), and [gclient source](https://chromium.googlesource.com/chromium/tools/depot_tools/+/d38909eb68d85b91259f4bf20fe5ebcd38461bc2/gclient.py).

Chromium and its dependencies retain their own licenses and notices in the source tree. Preserve them in redistributed source and browser packages.
