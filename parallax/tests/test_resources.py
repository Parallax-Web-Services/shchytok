"""Generated-header and resource-pack format cases used by the verifier."""

import gzip
import importlib.util
from contextlib import redirect_stdout
import io
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock


SPEC = importlib.util.spec_from_file_location(
    "verify_resources", Path(__file__).resolve().parents[1] / "tools/verify_resources.py")
verifier = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verifier)


class ResourceFormatsTest(unittest.TestCase):
    # Exact identity definitions from the first real non-official resource build.
    VERSION_HEADER = '''#define COMPANY_FULLNAME_STRING "Parallax Web Services"
#define COMPANY_SHORTNAME_STRING "Parallax Web Services"
#define PRODUCT_FULLNAME_STRING "Parallax"
#define PRODUCT_SHORTNAME_STRING "Parallax"
#define COPYRIGHT_STRING "Copyright @LASTCHANGE_YEAR@ The Chromium Authors. All rights reserved."
#define OFFICIAL_BUILD_STRING "0"
'''

    def test_real_nonofficial_version_header_preserves_upstream_placeholder(self):
        verifier.verify_version_header(self.VERSION_HEADER)
        verifier.verify_version_header(self.VERSION_HEADER.replace("@LASTCHANGE_YEAR@", "2026"))

    def test_version_header_still_rejects_changed_copyright_or_identity(self):
        for header in (
            self.VERSION_HEADER.replace("The Chromium Authors", "Different Authors"),
            self.VERSION_HEADER.replace("@LASTCHANGE_YEAR@", "@UNEXPECTED_YEAR@"),
            self.VERSION_HEADER.replace('PRODUCT_FULLNAME_STRING "Parallax"',
                                        'PRODUCT_FULLNAME_STRING "Chromium"'),
        ):
            with self.subTest(header=header), self.assertRaises(verifier.VerificationError):
                verifier.verify_version_header(header)

    def test_plain_and_allowlisted_resource_ids(self):
        self.assertEqual(verifier.resource_id("#define IDS_NAME 123\n", "IDS_NAME"), 123)
        self.assertEqual(verifier.resource_id(
            "#define IDS_NAME (::ui::AllowlistedResource<123>(), 123)\n", "IDS_NAME"), 123)

    def test_missing_duplicate_and_inconsistent_ids_fail(self):
        for header in (
            "#define IDS_OTHER 123\n",
            "#define IDS_NAME 123\n#define IDS_NAME 124\n",
            "#define IDS_NAME (::ui::AllowlistedResource<123>(), 124)\n",
        ):
            with self.subTest(header=header), self.assertRaises(verifier.VerificationError):
                verifier.resource_id(header, "IDS_NAME")

    def test_raw_and_gzipped_artwork(self):
        svg = b'<svg xmlns="http://www.w3.org/2000/svg" />'
        header = "#define IDR_LOGO 123\n"
        for payload in (svg, gzip.compress(svg)):
            pack = SimpleNamespace(resources={123: payload})
            self.assertEqual(verifier.resource_bytes(pack, header, "IDR_LOGO"), svg)

    def test_invalid_gzip_has_an_actionable_error(self):
        pack = SimpleNamespace(resources={123: b"\x1f\x8bcorrupt"})
        with self.assertRaisesRegex(verifier.VerificationError, "invalid gzip resource IDR_LOGO"):
            verifier.resource_bytes(pack, "#define IDR_LOGO 123\n", "IDR_LOGO")

    def test_missing_resource_is_identified(self):
        with self.assertRaisesRegex(verifier.VerificationError, "IDR_LOGO .* missing"):
            verifier.resource_bytes(SimpleNamespace(resources={}),
                                    "#define IDR_LOGO 123\n", "IDR_LOGO")

    def test_utf8_and_utf16_strings(self):
        reader = SimpleNamespace(UTF8=1, UTF16=2)
        for encoding, codec in ((1, "utf-8"), (2, "utf-16-le")):
            pack = SimpleNamespace(encoding=encoding, resources={123: "Parallax".encode(codec)})
            self.assertEqual(verifier.resource_text(pack, "#define IDS_NAME 123\n",
                                                    "IDS_NAME", reader), "Parallax")

    def test_binary_string_pack_and_invalid_text_fail(self):
        reader = SimpleNamespace(UTF8=1, UTF16=2)
        for encoding, payload in ((0, b"Parallax"), (1, b"\xff"), (2, b"\x00")):
            pack = SimpleNamespace(encoding=encoding, resources={123: payload})
            with self.subTest(encoding=encoding), self.assertRaises(verifier.VerificationError):
                verifier.resource_text(pack, "#define IDS_NAME 123\n", "IDS_NAME", reader)


class CompiledResourceVerificationTest(unittest.TestCase):
    """Exercise native OS verification with real GRIT-formatted fixture packs."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name) / "out"
        self.source = verifier.ROOT / "src"
        old_bytecode = sys.dont_write_bytecode
        sys.dont_write_bytecode = True
        sys.path.insert(0, str(self.source / "tools/grit"))
        try:
            self.reader = importlib.import_module("grit.format.data_pack")
        finally:
            sys.path.pop(0)
            sys.dont_write_bytecode = old_bytecode

        self.strings = {
            "IDS_PRODUCT_NAME": "Parallax",
            "IDS_SHORT_PRODUCT_NAME": "Parallax",
            "IDS_ABOUT_VERSION_COMPANY_NAME": "Parallax Web Services",
            "IDS_ABOUT_VERSION_COPYRIGHT": verifier.COPYRIGHT,
        }
        self.images = {
            "IDR_PRODUCT_LOGO_SVG": verifier.LOGO.read_bytes(),
            "IDR_PRODUCT_LOGO_ANIMATION_SVG": verifier.LOGO.read_bytes(),
            "IDR_PRODUCT_LOGO_64": (
                self.source / "chrome/app/theme/chromium/linux/product_logo_64.png"
            ).read_bytes(),
        }
        self.write_browser_packs()
        self.write("gen/chrome/common/chrome_version.h", ResourceFormatsTest.VERSION_HEADER)

    def write(self, relative, contents):
        path = self.output / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents, encoding="utf-8")

    def write_pack(self, header, pack, resources, encoding):
        items = list(enumerate(resources.items(), 1001))
        self.write(header, "".join(f"#define {name} {identifier}\n"
                                    for identifier, (name, _) in items))
        path = self.output / pack
        path.parent.mkdir(parents=True, exist_ok=True)
        self.reader.WriteDataPack(
            {identifier: value.encode("utf-8") if isinstance(value, str) else value
             for identifier, (_, value) in items},
            str(path), encoding,
        )

    def write_browser_packs(self):
        self.write_pack("gen/chrome/grit/branded_strings.h",
                        "gen/chrome/branded_strings_en-US.pak", self.strings, self.reader.UTF8)
        self.write_pack("gen/chrome/grit/chrome_unscaled_resources.h",
                        "gen/chrome/chrome_unscaled_resources.pak", self.images, self.reader.BINARY)

    def write_os_packs(self, *, product="ParallaxOS", notification="ParallaxOS"):
        self.strings["IDS_PRODUCT_OS_NAME"] = product
        self.write_browser_packs()
        self.write_pack("gen/ash/strings/grit/ash_strings.h",
                        "gen/ash/strings/ash_strings_en-US.pak",
                        {"IDS_ASH_MESSAGE_CENTER_SYSTEM_APP_NAME": notification}, self.reader.UTF8)

    def test_browser_verification_preserves_default_and_needs_no_os_resources(self):
        verifier.verify(self.source, self.output)
        verifier.verify(self.source, self.output, configuration="development")

    def test_os_verification_checks_both_real_pack_identities(self):
        self.write_os_packs()
        verifier.verify(self.source, self.output, configuration="os-development")

    def test_os_verification_rejects_either_upstream_os_identity(self):
        for field, expected in (("product", "IDS_PRODUCT_OS_NAME"),
                                ("notification", "IDS_ASH_MESSAGE_CENTER_SYSTEM_APP_NAME")):
            with self.subTest(field=field):
                self.write_os_packs(**{field: "ChromiumOS"})
                with self.assertRaisesRegex(verifier.VerificationError, expected):
                    verifier.verify(self.source, self.output, configuration="os-development")

    def test_os_verification_requires_ash_generated_artifacts(self):
        self.write_os_packs()
        (self.output / "gen/ash/strings/ash_strings_en-US.pak").unlink()
        with self.assertRaisesRegex(verifier.VerificationError, "ash_strings_en-US.pak"):
            verifier.verify(self.source, self.output, configuration="os-development")

    def test_os_verification_preserves_company_and_artwork_checks(self):
        self.write_os_packs()
        self.strings["IDS_ABOUT_VERSION_COMPANY_NAME"] = "The Chromium Authors"
        self.write_browser_packs()
        with self.assertRaisesRegex(verifier.VerificationError, "IDS_ABOUT_VERSION_COMPANY_NAME"):
            verifier.verify(self.source, self.output, configuration="os-development")
        self.strings["IDS_ABOUT_VERSION_COMPANY_NAME"] = "Parallax Web Services"
        self.images["IDR_PRODUCT_LOGO_SVG"] = b"<svg>different artwork</svg>"
        self.write_browser_packs()
        with self.assertRaisesRegex(verifier.VerificationError, "IDR_PRODUCT_LOGO_SVG"):
            verifier.verify(self.source, self.output, configuration="os-development")


class ResourceCommandLineTest(unittest.TestCase):
    def test_each_configuration_selects_its_own_default_output(self):
        source = Path("example-src")
        for configuration, output in verifier.OUTPUTS.items():
            with self.subTest(configuration=configuration), mock.patch.object(verifier, "verify") as verify:
                with redirect_stdout(io.StringIO()):
                    result = verifier.main(["--source-root", str(source),
                                            "--configuration", configuration])
                self.assertEqual(result, 0)
                verify.assert_called_once_with(source, source / "out" / output,
                                               configuration=configuration)

    def test_existing_output_override_and_default_browser_mode_are_preserved(self):
        source, output = Path("example-src"), Path("custom-output")
        with mock.patch.object(verifier, "verify") as verify, redirect_stdout(io.StringIO()):
            self.assertEqual(verifier.main(["--source-root", str(source),
                                            "--output-dir", str(output)]), 0)
        verify.assert_called_once_with(source, output, configuration="release")


if __name__ == "__main__":
    unittest.main()
