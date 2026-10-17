# ParallaxOS

ParallaxOS is a browser-first business operating environment under development. Its intended experience opens into Parallax, bringing workspaces, web apps, remote applications, and business tools into one session.

The native system shell is **Ash**, the ChromeOS window manager and system UI already included in this Chromium fork. **Helm** is intended to supply business interface components within that environment. These have separate responsibilities: Ash owns windows, the shelf, launcher, notifications, and system controls; the Parallax workspace owns business navigation and work.

## Current development target

`os-development` builds Chromium's Linux-hosted ChromeOS configuration from the same pinned source as the Parallax browser, with `target_os = "chromeos"` and `is_chromeos_device = false`. Its output is `src/out/ParallaxOS/chrome`. It has a separate user profile at `$XDG_CONFIG_HOME/parallaxos-development`, normally `~/.config/parallaxos-development`.

```sh
# Run from the repository root with the branding virtual environment active.
python3 parallax/tools/build.py bootstrap
python3 parallax/tools/build.py sync
python3 parallax/tools/build.py brand
python3 parallax/tools/build.py configure --configuration os-development
python3 parallax/tools/build.py build --configuration os-development
python3 parallax/tools/launch.py --configuration os-development
```

The shared dependency configuration includes Linux and ChromeOS. It downloads public ChromeOS development dependencies without selecting a device-board SDK or VM image. Existing `release` and `development` browser configurations retain their own output and profile directories.

This is the upstream **linux-chromeos development shell**: the system UI runs in a host window, with most device services stubbed. The default development session is not Parallax authentication. This target does not produce a bootable operating-system image, device enrollment, or functioning power/network management services.

## Validate native system branding

After dependency sync and configuration, these focused targets compile the native system strings and shared browser artwork without compiling the complete shell. Run from the repository root:

```sh
export DEPOT_TOOLS_UPDATE=0
export PATH="$PWD/parallax/.cache/depot_tools:$PATH"
cd src
autoninja -C out/ParallaxOS chrome/app:branded_strings_grit \
  chrome/app/theme:chrome_unscaled_resources_grit \
  chrome/app/theme:theme_resources_grit chrome/common:version_header \
  ash/strings:strings_grit
cd ..
python3 parallax/tools/verify_resources.py --source-root src --configuration os-development
```

The verifier checks `IDS_PRODUCT_OS_NAME` in `gen/chrome/branded_strings_en-US.pak` and `IDS_ASH_MESSAGE_CENTER_SYSTEM_APP_NAME` in `gen/ash/strings/ash_strings_en-US.pak`, both against `ParallaxOS`. It also retains the browser product name, company, Chromium copyright, generated version identity, and original orbital artwork checks. `--output-dir` can select a different output path while keeping these OS checks. Passing resource verification does not establish that the complete shell compiles, launches, or boots.

## Product direction

The intended workstation experience is:

1. Boot into the Parallax sign-in and session experience.
2. Restore the user's company workspace, applications, and working context.
3. Launch web applications and remote applications through the same system surface.
4. Use Helm for business operations and agent work while ordinary browsing remains available.
5. Keep workspace state portable between approved devices, with local profiles and cached work scoped to the user.

These are product requirements, not capabilities claimed by the current development build. A real Parallax identity/session integration, application registry, workspace persistence, remote-compute connections, and device management still need implementation.

The sibling Helm project currently provides a Laravel/Inertia/React command-deck application and reusable interface components. Its configured workspace navigation is not an OS application registry. Its Nexus-backed web authentication is an integration starting point; it does not establish an operating-system user session.

The sibling `parallax-app` control plane already provides business workspace screens and an application catalogue. The first application integration should open those hosted screens in app windows and reuse their existing browser authentication. `GET /api/suite` is a public product catalogue, and `/go/{product}` performs authenticated launch tracking and redirection. A personalized native launcher needs a separate authenticated contract for user membership and entitlements; the public catalogue cannot supply that information. The existing activity ingest endpoint is a server-to-server interface, not a device feed.

## Path to a bootable system

The next platform milestone is a **ChromiumOS-based VM image**, initially targeting `amd64-generic`. That requires a separate ChromiumOS manifest checkout, a pinned OS buildspec, board packages, Parallax browser integration, image construction, and a VM boot test.

The image work must also supply the real system services and lifecycle that the hosted Ash build stubs: user-session storage, networking, device policy, verified boot, updates, and recovery. Those are platform integration work, separate from the hosted shell target.

The intended sequence is:

- Validate the native ParallaxOS shell build and product branding.
- Integrate Parallax control-plane screens and Helm business components, and define the app/workspace interfaces.
- Implement Parallax sign-in and persistent user sessions.
- Build and boot a ChromiumOS VM image with the Parallax browser.
- Add device enrollment, update/recovery infrastructure, and hardware targets.

Upstream references: [pinned ChromeOS/Ash development instructions](https://chromium.googlesource.com/chromium/src/+/5869fe313571d75152b77813cf0a4ee26a4e6df0/docs/chromeos_build_instructions.md), [ChromiumOS developer guide](https://www.chromium.org/chromium-os/developer-library/guides/development/developer-guide/).
