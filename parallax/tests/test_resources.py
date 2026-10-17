"""Generated-header and resource-pack format cases used by the verifier."""

import gzip
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest


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


if __name__ == "__main__":
    unittest.main()
