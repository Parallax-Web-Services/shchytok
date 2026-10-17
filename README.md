# Parallax

Parallax is a native Chromium browser fork for Linux x86-64. The complete Chromium **155.0.8059.84** source tree lives in `src/`, imported as a single source snapshot. Chromium's upstream history is not merged into this repository. Dependencies use the exact revisions in `src/DEPS`.

**ParallaxOS** adds a native ChromeOS/Ash development target to the same source tree. Its system model is a browser-first workstation with a shelf, launcher, application windows, and a business workspace. See [the ParallaxOS guide](parallax/os/README.md) for the development target and the path to a bootable ChromiumOS-based system.

The browser uses Parallax's existing orbital logo from the marketing repository, with its original SVG preserved in `parallax/branding/`. Product names, Linux profile storage, desktop identity, and application icons are rebranded in the Chromium source. The internal build target remains `chrome`.

See [the build guide](parallax/README.md) for prerequisites, dependency synchronization, compilation, launching, and source maintenance. The initial supported build target is Linux; Windows and macOS have not been validated.

```bash
python3 -m venv parallax/.build/branding-venv
source parallax/.build/branding-venv/bin/activate
pip install -r parallax/branding/requirements.txt
python parallax/tools/build.py bootstrap
python parallax/tools/build.py sync
python parallax/tools/build.py brand
python parallax/tools/build.py build
python parallax/tools/launch.py
```

Build dependencies must first be installed as described in the build guide. The workflow preserves Chromium's sandbox and uses a separate Parallax profile.

Immutable Chromium and depot_tools revisions, plus the original logo checksum, are recorded in [parallax/upstream.json](parallax/upstream.json). Source-import tooling is in [parallax/tools/import_upstream.py](parallax/tools/import_upstream.py). Importing a fresh snapshot requires Python 3.12 or later; normal build tooling and branding support Python 3.10 or later.

Parallax additions use the root [BSD license](LICENSE). Chromium retains [its license](src/LICENSE), third-party notices, and the built browser's `chrome://credits` attribution.
