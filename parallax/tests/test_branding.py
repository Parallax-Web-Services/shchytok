"""Offline behavior tests for the pinned Chromium branding operation.

The C++ and BRANDING fixture contexts below are excerpts from the official
155.0.8059.84 tag. The small GRD exercises real upstream patterns together with
adversarial examples that must remain untouched. Tests use independent expected
outputs; the production manifest still requires the complete upstream files.
"""

from __future__ import annotations

from contextlib import redirect_stderr, redirect_stdout
import importlib.util
import io
import os
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest import mock


TOOL = Path(__file__).resolve().parents[1] / "tools" / "apply_branding.py"
SPEC = importlib.util.spec_from_file_location("parallax_apply_branding", TOOL)
assert SPEC is not None and SPEC.loader is not None
branding = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = branding
SPEC.loader.exec_module(branding)


IDENTITY = """COMPANY_FULLNAME=The Chromium Authors
COMPANY_SHORTNAME=The Chromium Authors
PRODUCT_FULLNAME=Chromium
PRODUCT_SHORTNAME=Chromium
PRODUCT_INSTALLER_FULLNAME=Chromium Installer
PRODUCT_INSTALLER_SHORTNAME=Chromium Installer
COPYRIGHT=Copyright @LASTCHANGE_YEAR@ The Chromium Authors. All rights reserved.
MAC_BUNDLE_ID=org.chromium.Chromium
MAC_CREATOR_CODE=Cr24
MAC_TEAM_ID=
"""
BRANDED_IDENTITY = """COMPANY_FULLNAME=Parallax Web Services
COMPANY_SHORTNAME=Parallax Web Services
PRODUCT_FULLNAME=Parallax
PRODUCT_SHORTNAME=Parallax
PRODUCT_INSTALLER_FULLNAME=Parallax Installer
PRODUCT_INSTALLER_SHORTNAME=Parallax Installer
COPYRIGHT=Copyright @LASTCHANGE_YEAR@ The Chromium Authors. All rights reserved.
MAC_BUNDLE_ID=org.chromium.Chromium
MAC_CREATOR_CODE=Cr24
MAC_TEAM_ID=
"""

MESSAGES = """<?xml version="1.0" encoding="UTF-8"?>
<grit><messages fallback_to_english="true">
  <!-- Preserve <message name="IDS_EXAMPLE">Chromium</message> examples. -->
  <message name="IDS_PRODUCT_NAME" desc="Chromium application > name">Chromium</message>
  <message name ="IDS_GLIC_INELIGIBLE_PROFILE_NOTICE">Use Gemini in Chromium</message>
  <message name='IDS_ABOUT'>About &amp;Chromium</message>
  <message name="IDS_BROWSER_WINDOW_TITLE_FORMAT"><ph name="PAGE_TITLE">$1<ex>Chromium</ex></ph> - Chromium</message>
  <message name="IDS_LINK"><a href="https://example.org/Chromium">Use Chromium</a><!-- Chromium > Chromium --></message>
  <message name="IDS_ABOUT_VERSION_COMPANY_NAME">Google LLC</message>
  <message name="IDS_ABOUT_VERSION_COMPANY_NAME">The Chromium Authors</message>
  <message name="IDS_ABOUT_VERSION_COPYRIGHT">Copyright The Chromium Authors.</message>
  <message name="IDS_ABOUT_CROS_VERSION_LICENSE">ChromiumOS and Chromium credits</message>
  <message name="IDS_ABOUT_TERMS_OF_SERVICE">Not used in Chromium. Placeholder to keep resource maps in sync.</message>
  <message name="IDS_UPDATER">ChromiumUpdater.exe is required</message>
  <message name="IDS_PRODUCT_OS_NAME">ChromiumOS</message>
</messages></grit>
"""
BRANDED_MESSAGES = """<?xml version="1.0" encoding="UTF-8"?>
<grit><messages fallback_to_english="true">
  <!-- Preserve <message name="IDS_EXAMPLE">Chromium</message> examples. -->
  <message name="IDS_PRODUCT_NAME" desc="Chromium application > name">Parallax</message>
  <message name ="IDS_GLIC_INELIGIBLE_PROFILE_NOTICE">Use Gemini in Parallax</message>
  <message name='IDS_ABOUT'>About &amp;Parallax</message>
  <message name="IDS_BROWSER_WINDOW_TITLE_FORMAT"><ph name="PAGE_TITLE">$1<ex>Chromium</ex></ph> - Parallax</message>
  <message name="IDS_LINK"><a href="https://example.org/Chromium">Use Parallax</a><!-- Chromium > Chromium --></message>
  <message name="IDS_ABOUT_VERSION_COMPANY_NAME">Google LLC</message>
  <message name="IDS_ABOUT_VERSION_COMPANY_NAME">Parallax Web Services</message>
  <message name="IDS_ABOUT_VERSION_COPYRIGHT">Copyright The Chromium Authors.</message>
  <message name="IDS_ABOUT_CROS_VERSION_LICENSE">ChromiumOS and Chromium credits</message>
  <message name="IDS_ABOUT_TERMS_OF_SERVICE">Not used in Chromium. Placeholder to keep resource maps in sync.</message>
  <message name="IDS_UPDATER">ChromiumUpdater.exe is required</message>
  <message name="IDS_PRODUCT_OS_NAME">ChromiumOS</message>
</messages></grit>
"""

PROFILE = """#if BUILDFLAG(GOOGLE_CHROME_FOR_TESTING_BRANDING)
  std::string data_dir_basename = "google-chrome-for-testing";
#elif BUILDFLAG(GOOGLE_CHROME_BRANDING)
  std::string data_dir_basename = "google-chrome";
#else
  std::string data_dir_basename = "chromium";
#endif
"""
BRANDED_PROFILE = """#if BUILDFLAG(GOOGLE_CHROME_FOR_TESTING_BRANDING)
  std::string data_dir_basename = "google-chrome-for-testing";
#elif BUILDFLAG(GOOGLE_CHROME_BRANDING)
  std::string data_dir_basename = "google-chrome";
#else
  std::string data_dir_basename = "parallax";
#endif
"""

DESKTOP = """std::string GetDesktopName(base::Environment* env) {
  return "chromium-browser.desktop";
#endif
}
"""
BRANDED_DESKTOP = """std::string GetDesktopName(base::Environment* env) {
  return "parallax.desktop";
#endif
}
"""

ICON = """std::string GetIconName() {
#if BUILDFLAG(GOOGLE_CHROME_BRANDING)
  return "google-chrome";
#else  // BUILDFLAG(CHROMIUM_BRANDING)
  return "chromium-browser";
#endif
}
std::string GetDirectLaunchUrlScheme() {
#else
  return "chromium";
#endif
}
"""
BRANDED_ICON = """std::string GetIconName() {
#if BUILDFLAG(GOOGLE_CHROME_BRANDING)
  return "google-chrome";
#else  // BUILDFLAG(CHROMIUM_BRANDING)
  return "parallax";
#endif
}
std::string GetDirectLaunchUrlScheme() {
#else
  return "chromium";
#endif
}
"""

FIXTURES = (
    ("chrome/app/theme/chromium/BRANDING", IDENTITY, BRANDED_IDENTITY, branding.brand_identity),
    ("chrome/app/chromium_strings.grd", MESSAGES, BRANDED_MESSAGES, branding.brand_messages),
    ("chrome/common/chrome_paths_linux.cc", PROFILE, BRANDED_PROFILE, branding.brand_linux_profile),
    ("chrome/common/channel_info_posix.cc", DESKTOP, BRANDED_DESKTOP, branding.brand_linux_desktop),
    ("chrome/browser/shell_integration_linux.cc", ICON, BRANDED_ICON, branding.brand_linux_icon),
)


class BrandingTransformsTest(unittest.TestCase):
    def test_reviewed_contexts_and_protected_content(self):
        for path, before, expected, transform in FIXTURES:
            with self.subTest(path=path):
                self.assertEqual(transform(before), expected)

    def test_context_drift_and_duplicate_are_rejected(self):
        with self.assertRaises(branding.BrandingError):
            branding.brand_linux_profile(PROFILE.replace('"chromium"', '"different"'))
        with self.assertRaises(branding.BrandingError):
            branding.brand_linux_profile(PROFILE + PROFILE)

    def test_message_transform_is_idempotent(self):
        self.assertEqual(branding.brand_messages(BRANDED_MESSAGES), BRANDED_MESSAGES)

    def test_malformed_xml_is_rejected(self):
        with self.assertRaises(branding.ET.ParseError):
            branding.brand_messages('<grit><message name="IDS_PRODUCT_NAME">Chromium')

    def test_production_manifest_is_immutable_and_complete(self):
        self.assertEqual(branding.UPSTREAM_VERSION, "155.0.8059.84")
        self.assertEqual(len(branding.INPUTS), 6)
        self.assertIn("chrome/app/settings_chromium_strings.grdp",
                      {entry.path for entry in branding.INPUTS})
        for entry in branding.INPUTS:
            self.assertRegex(entry.upstream_sha256, r"^[0-9a-f]{64}$")
            self.assertRegex(entry.branded_sha256, r"^[0-9a-f]{64}$")
            self.assertNotEqual(entry.upstream_sha256, entry.branded_sha256)


class BrandingOperationTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="parallax-branding-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.inputs = []
        for name, before, after, transform in FIXTURES:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(before.encode())
            self.inputs.append(branding.Input(
                name, branding.digest(before.encode()), branding.digest(after.encode()), transform))
        self.inputs = tuple(self.inputs)

    def snapshot(self):
        return {path.relative_to(self.root).as_posix(): path.read_bytes()
                for path in self.root.rglob("*") if path.is_file()}

    def run_cli(self, *arguments):
        with mock.patch.object(branding, "INPUTS", self.inputs):
            with redirect_stdout(io.StringIO()) as stdout, redirect_stderr(io.StringIO()) as stderr:
                status = branding.main(["--source-root", str(self.root), *arguments])
        return status, stdout.getvalue(), stderr.getvalue()

    def test_apply_then_check_and_repeat_preserve_bytes_and_mtime(self):
        self.assertEqual(self.run_cli()[0], 0)
        before = self.snapshot()
        mtimes = {entry.path: (self.root / entry.path).stat().st_mtime_ns for entry in self.inputs}
        self.assertEqual(self.run_cli("--check")[0], 0)
        status, output, _ = self.run_cli()
        self.assertEqual(status, 0)
        self.assertIn("0 file(s) changed", output)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(mtimes, {
            entry.path: (self.root / entry.path).stat().st_mtime_ns for entry in self.inputs})

    def test_check_unapplied_is_read_only(self):
        before = self.snapshot()
        status, output, _ = self.run_cli("--check")
        self.assertEqual(status, 1)
        self.assertIn("Branding is required", output)
        self.assertEqual(self.snapshot(), before)

    def test_drift_in_last_file_prevents_every_write(self):
        path = self.root / self.inputs[-1].path
        path.write_bytes(path.read_bytes() + b"// local change\n")
        before = self.snapshot()
        status, _, error = self.run_cli()
        self.assertEqual(status, 2)
        self.assertIn("source drift", error)
        self.assertEqual(self.snapshot(), before)

    def test_missing_file_prevents_every_write(self):
        (self.root / self.inputs[-1].path).unlink()
        before = self.snapshot()
        self.assertEqual(self.run_cli()[0], 2)
        self.assertEqual(self.snapshot(), before)

    def test_wrong_transform_output_prevents_every_write(self):
        inputs = (*self.inputs[:-1], branding.Input(
            self.inputs[-1].path, self.inputs[-1].upstream_sha256,
            "0" * 64, self.inputs[-1].transform))
        before = self.snapshot()
        with self.assertRaisesRegex(branding.BrandingError, "unexpected branding output"):
            branding.preflight(self.root, inputs)
        self.assertEqual(self.snapshot(), before)

    def test_partial_previous_application_resumes(self):
        first = self.root / self.inputs[0].path
        first.write_bytes(BRANDED_IDENTITY.encode())
        first_mtime = first.stat().st_mtime_ns
        self.assertEqual(self.run_cli()[0], 0)
        self.assertEqual(first.stat().st_mtime_ns, first_mtime)
        self.assertEqual(self.run_cli("--check")[0], 0)

    def test_concurrent_edit_prevents_replacements_and_cleans_staging(self):
        changes = branding.preflight(self.root, self.inputs)
        path = changes[-1].path
        path.write_bytes(path.read_bytes() + b"// concurrent edit\n")
        before = self.snapshot()
        with self.assertRaisesRegex(branding.BrandingError, "changed after preflight"):
            branding.apply_changes(changes)
        self.assertEqual(self.snapshot(), before)

    def test_failed_replace_rolls_back_and_cleans_staging(self):
        changes = branding.preflight(self.root, self.inputs)
        before = self.snapshot()
        real_replace = os.replace
        calls = 0

        def fail_once(source, destination):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError("simulated replacement failure")
            return real_replace(source, destination)

        with mock.patch.object(branding.os, "replace", side_effect=fail_once):
            with self.assertRaisesRegex(OSError, "simulated replacement failure"):
                branding.apply_changes(changes)
        self.assertEqual(self.snapshot(), before)

    @unittest.skipIf(os.name == "nt", "Windows does not preserve POSIX permission bits")
    def test_file_modes_are_preserved(self):
        path = self.root / self.inputs[0].path
        path.chmod(0o640)
        self.assertEqual(self.run_cli()[0], 0)
        self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o640)

    def test_source_symlink_is_rejected(self):
        path = self.root / self.inputs[-1].path
        path.unlink()
        try:
            path.symlink_to(self.root / self.inputs[0].path)
        except OSError as exc:
            self.skipTest(f"symlink creation unavailable: {exc}")
        before = self.snapshot()
        self.assertEqual(self.run_cli()[0], 2)
        self.assertEqual(self.snapshot(), before)


if __name__ == "__main__":
    unittest.main()
