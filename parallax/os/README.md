# Parallax Station

Parallax Station is a browser-first business operating environment under development. Its intended experience opens into Parallax, bringing workspaces, web apps, remote applications, and business tools into one session.

The native system shell is **Ash**, the ChromeOS window manager and system UI already included in this Chromium fork. **Helm** is intended to supply business interface components within that environment. These have separate responsibilities: Ash owns windows, the shelf, launcher, notifications, and system controls; the Parallax workspace owns business navigation and work.

## Current development target

`os-development` builds Chromium's Linux-hosted ChromeOS configuration from the same pinned source as the Parallax browser, with `target_os = "chromeos"` and `is_chromeos_device = false`. Its output is `src/out/ParallaxStation/chrome`. It has a separate user profile at `$XDG_CONFIG_HOME/parallax-station-development`, normally `~/.config/parallax-station-development`.

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
autoninja -C out/ParallaxStation chrome/app:branded_strings_grit \
  chrome/app/theme:chrome_unscaled_resources_grit \
  chrome/app/theme:theme_resources_grit chrome/common:version_header \
  ash/strings:strings_grit
cd ..
python3 parallax/tools/verify_resources.py --source-root src --configuration os-development
```

The verifier checks `IDS_PRODUCT_OS_NAME` in `gen/chrome/branded_strings_en-US.pak` and `IDS_ASH_MESSAGE_CENTER_SYSTEM_APP_NAME` in `gen/ash/strings/ash_strings_en-US.pak`, both against `Parallax Station`. It also retains the browser product name, company, Chromium copyright, generated version identity, and original orbital artwork checks. `--output-dir` can select a different output path while keeping these OS checks. Passing resource verification does not establish that the complete shell compiles, launches, or boots.

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

- Validate the native Parallax Station shell build and product branding.
- Integrate Parallax control-plane screens and Helm business components, and define the app/workspace interfaces.
- Implement Parallax sign-in and persistent user sessions.
- Build and boot a ChromiumOS VM image with the Parallax browser.
- Add device enrollment, update/recovery infrastructure, and hardware targets.

Upstream references: [pinned ChromeOS/Ash development instructions](https://chromium.googlesource.com/chromium/src/+/5869fe313571d75152b77813cf0a4ee26a4e6df0/docs/chromeos_build_instructions.md), [ChromiumOS developer guide](https://www.chromium.org/chromium-os/developer-library/guides/development/developer-guide/).

## Windows reference VM with WHPX

A native Windows QEMU guest provides a separate reference environment while the Parallax Station shell and bootable-image work continues. Supply an independently downloaded, checksum-verified **raw upstream image**. This helper does not download an image or turn that upstream guest into a branded Parallax Station build.

The helper uses Windows Hypervisor Platform (WHPX), 4 virtual CPUs, 8 GiB RAM, virtio SCSI storage, virtio network/RNG/VGA devices, and a GTK window. It creates a writable qcow2 overlay; the verified raw base remains unchanged.

The initial `max` CPU trial initialized WHPX but the executing guest paused with an unrecoverable virtual-processor exit and XSAVE-state errors. The conservative default now exposes the required SSSE3/SSE4, AES, PCLMULQDQ, POPCNT, CX16 and LAHF-LM features while disabling XSAVE and AVX:

```text
qemu64,+ssse3,+sse4.1,+sse4.2,+aes,+pclmulqdq,+popcnt,+cx16,+lahf_lm,-xsave,-xsaveopt,-xsavec,-xsaves,-avx,-avx2
```

This conservative baseline has booted the upstream **R155-16820.30.0** test image (**Chromium 155.0.8059.78**) headlessly on the local Windows WHPX host. Validation confirmed an active fake test session and a framebuffer screenshot showing the Ash shelf, launcher, clock and a Chromium browser window at `chrome://version`. The same desktop check passed after a clean shutdown and restart with the final helper in GTK window mode. This is upstream reference-guest evidence; the repository's Chromium **155.0.8059.84** Parallax Station browser/system build has not been boot-validated.

The reference test session used the official test image's `autologin.py -w` flow and upstream public test access. This establishes a fake development session without exercising a personal account or Parallax authentication. No key material or credentials are included here.

`-CpuModel` can select an explicit alternative for a controlled compatibility trial. The VM state records the selected CPU string so results can be attributed to the configuration that ran.

Run the trusted repository helper from a local Windows copy under the existing execution policy. Copying it avoids treating an unsigned script on the WSL UNC share as a remote script; no execution-policy bypass is needed. The state directory is explicit because the helper's local copy is outside the checkout.

```powershell
$repo = '\\wsl.localhost\Ubuntu\mnt\wsl\projects\workspace\parallax\shchytok'
$helper = Join-Path $env:TEMP 'parallax-windows-vm.ps1'
Copy-Item -LiteralPath "$repo\parallax\tools\windows_vm.ps1" -Destination $helper -Force
& $helper -Action doctor -QemuPath 'C:\msys64\ucrt64\bin\qemu-system-x86_64.exe'

# Use the verified, decompressed reference image in the workspace cache.
$image = "$repo\parallax\.build\vm\R155-16820.30.0\chromiumos_test_image.bin"
$state = "$repo\parallax\.build\vm\reference"
& $helper -Action start -ImagePath $image -StateDirectory $state
```

Keep the large base and overlay in the workspace cache; the Windows temporary copy is only the small PowerShell helper. `-QemuPath` accepts either the executable or its directory. Without it, discovery checks `PATH`, `C:\msys64\ucrt64\bin`, then `%ProgramFiles%\qemu`. `doctor` reports QEMU versions and whether its accelerator list includes WHPX; it starts no VM, installs nothing, and changes no Windows features.

The default `-DisplayMode gtk` opens the interactive guest window. For a headless compatibility run, use `-DisplayMode none`; the QEMU helper process is hidden and opens no GTK window. Its virtio VGA framebuffer remains available through the loopback QMP `screendump` command after the normal QMP greeting/capabilities handshake. VM state records the selected display mode. Changing the mode applies to the next start; it does not change another running guest.

The default ports are QMP at **127.0.0.1:4444** and guest SSH forwarding at **127.0.0.1:2222**. Both bind only to Windows loopback. Use `-QmpPort` or `-SshPort` to select different ports. SSH works only if the supplied guest enables an SSH server; the helper creates no accounts or credentials.

The state directory contains `reference-overlay.qcow2`, `vm-state.json` (own PID, UUID, CPU model, display mode, image/overlay paths and loopback ports), and `qemu-stdout.log`/`qemu-stderr.log`. A start refuses an active recorded PID, an occupied port, or an existing overlay with a different backing image/format. Startup failures include captured stderr. A live process after the startup check establishes only that QEMU is still running, not that the guest has booted.

Shut down through the guest normally before reusing its state directory. The helper exposes no process-kill action and never stops another QEMU process. Preserve the raw base at the same path: its overlay depends on that backing file. Keep reference-guest results separate from validation of a branded Parallax Station image.
