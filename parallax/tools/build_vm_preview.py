#!/usr/bin/env python3
"""Apply staged artwork to a separate, local developer VM image.

Requires root for loop mounts. Leaves the pinned reference image untouched.
Selects the upstream non-verified developer boot entry for this preview copy;
this artifact is not a verified-boot distribution or a full Station OS build.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
from contextlib import contextmanager

ROOT = Path(__file__).resolve().parents[2]
REFERENCE = ROOT / "parallax/.build/vm/R155-16820.30.0/chromiumos_test_image.bin"
DESTINATION = Path("/mnt/d/Parallax/StationPreview")


def run(*args: str) -> str:
    result = subprocess.run(args, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise RuntimeError(f"{args[0]} failed: {result.stdout}\n{result.stderr}")
    return result.stdout


@contextmanager
def partition(image: Path, part: dict, destination: Path, filesystem: str):
    device = run("losetup", "--find", "--show", "--offset", str(part["start"] * 512),
                 "--sizelimit", str(part["size"] * 512), str(image)).strip()
    destination.mkdir(parents=True, exist_ok=True)
    mounted = False
    try:
        run("mount", "-t", filesystem, device, str(destination))
        mounted = True
        yield destination
    finally:
        if mounted:
            run("umount", str(destination))
        run("losetup", "--detach", device)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--staged", type=Path, required=True)
    args = parser.parse_args()
    if os.geteuid() != 0:
        parser.error("Run as root for the isolated image loop mounts")
    staged = args.staged.resolve(strict=True)
    report = json.loads((staged / "branding-report.json").read_text())
    if report["kind"] != "parallax-station-branding-preview":
        raise ValueError("Wrong branding artifact kind")
    image = DESTINATION / "station-preview.bin"
    partial = DESTINATION / "station-preview.bin.partial"
    if image.exists() or partial.exists():
        raise ValueError("Existing preview image or partial image is preserved; use a fresh destination")
    if REFERENCE.stat().st_size != 11102403072:
        raise ValueError("Reference size does not match the verified archive extraction")
    parts = json.loads(run("sfdisk", "--json", str(REFERENCE)))["partitiontable"]["partitions"]
    parts = {p["name"]: p for p in parts}
    if parts["ROOT-A"]["start"] != 712704 or parts["EFI-SYSTEM"]["start"] != 446464:
        raise ValueError("Unexpected reference partition layout")
    DESTINATION.mkdir(parents=True, exist_ok=True)
    if shutil.disk_usage(DESTINATION).free < REFERENCE.stat().st_size + 5 * 1024**3:
        raise ValueError("Insufficient free space for the separate preview copy")
    print("Copying pinned reference to a separate preview image", flush=True)
    run("cp", "--sparse=always", "--reflink=auto", str(REFERENCE), str(partial))

    # The upstream developer tool common_minimal.sh:enable_rw_mount clears
    # the high byte of ext's ro_compat flags at offset 0x467. Apply that same
    # change only to this unmounted preview copy, never to a host block device.
    offset = parts["ROOT-A"]["start"] * 512
    with partial.open("r+b") as stream:
        stream.seek(offset + 0x438)
        if stream.read(2) != b"\x53\xef":
            raise ValueError("ROOT-A is not the expected ext filesystem")
        stream.seek(offset + 0x467)
        original_flag = stream.read(1)
        if original_flag not in (b"\xff", b"\x00"):
            raise ValueError("Unexpected rootfs developer read-only flag")
        stream.seek(offset + 0x467)
        stream.write(b"\x00")

    mount_root = Path("/mnt/wsl/station-build/preview-mount")
    print("Applying verified artwork to preview ROOT-A", flush=True)
    with partition(partial, parts["ROOT-A"], mount_root / "root", "ext4") as root:
        for entry in report["packs"] + report["boot_frames"]:
            relative = Path(entry["path"])
            if relative.is_absolute() or ".." in relative.parts:
                raise ValueError("Invalid staged path")
            target = root / relative
            if target.is_symlink() or not target.resolve().is_relative_to(root.resolve()):
                raise ValueError("Staged target escapes the preview root")
            if hashlib.sha256(target.read_bytes()).hexdigest() != entry["before_sha256"]:
                raise ValueError(f"Reference artwork mismatch at {relative}")
            replacement = (staged / relative).read_bytes()
            if hashlib.sha256(replacement).hexdigest() != entry["after_sha256"]:
                raise ValueError(f"Staged artwork mismatch at {relative}")
            target.write_bytes(replacement)
        marker = Path("etc/parallax-station-preview.json")
        (root / marker).write_bytes((staged / marker).read_bytes())
        (root / marker).chmod(0o644)
    print("Selecting the existing upstream developer boot entry", flush=True)
    with partition(partial, parts["EFI-SYSTEM"], mount_root / "efi", "vfat") as efi:
        config = efi / "syslinux/default.cfg"
        old_config = config.read_text()
        usb = (efi / "syslinux/usb.A.cfg").read_text()
        if "label chromeos-usb.A" not in usb or "root=" not in usb:
            raise ValueError("Missing upstream developer boot entry")
        config.write_text("DEFAULT chromeos-usb.A\n")
        grub = efi / "efi/boot/grub.cfg"
        old_grub = grub.read_text()
        if 'menuentry "local image A"' not in old_grub:
            raise ValueError("Missing upstream local-image UEFI boot entry")
        grub.write_text(old_grub.replace("defaultA=2", "defaultA=0").replace("defaultB=3", "defaultB=0"))
        report["boot"] = {"syslinux_before": old_config, "syslinux_after": config.read_text(),
                          "rootfs_verification": "disabled for this developer preview only",
                          "rootfs_ro_flag_before": original_flag.hex()}
        print(f"Developer boot configuration:\n{usb}", flush=True)
    partial.rename(image)
    report["image"] = str(image)
    report["reference_image"] = str(REFERENCE)
    report["complete"] = True
    (DESTINATION / "branding-report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"Preview image ready: {image}", flush=True)


if __name__ == "__main__":
    main()
