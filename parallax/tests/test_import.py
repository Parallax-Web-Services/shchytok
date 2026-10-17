"""Import isolation and archive traversal regression checks."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "import_upstream", Path(__file__).resolve().parents[1] / "tools/import_upstream.py")
importer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(importer)


class ArchiveSafetyTests(unittest.TestCase):
    def test_rejects_paths_outside_source(self):
        for name in ("../LICENSE", "/etc/passwd", "ok/../../escape", ".git/config"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                importer.safe_member(tarfile.TarInfo(name))

    def test_rejects_escaping_links_but_allows_relative_links(self):
        member = tarfile.TarInfo("tools/link")
        member.type = tarfile.SYMTYPE
        for target in ("../../outside", "/etc/passwd"):
            member.linkname = target
            with self.subTest(target=target), self.assertRaises(ValueError):
                importer.safe_member(member)
        member.linkname = "../build"
        importer.safe_member(member)


class SourceImportTests(unittest.TestCase):
    def test_import_preserves_outer_history_and_refuses_existing_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def git(*args):
                return subprocess.check_output(
                    ["git", "-c", "user.name=Import Test", "-c", "user.email=test@example.invalid",
                     "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", *args],
                    cwd=root, text=True).strip()
            git("init", "--quiet")
            (root / "LICENSE").write_text("Upstream license\n")
            (root / "DEPS").write_text("git_dependencies = 'SYNC'\n")
            (root / "chrome").mkdir()
            (root / "chrome/VERSION").write_text("MAJOR=155\n")
            git("add", ".")
            git("commit", "--quiet", "-m", "Fixture upstream")
            revision = git("rev-parse", "HEAD")
            # Host Git settings must not change the imported source bytes.
            git("config", "core.autocrlf", "true")
            index_before = (root / ".git/index").read_bytes()
            pin = {"source_directory": "src", "revision": revision, "tag": "fixture"}
            importer.import_tree(root, pin, fetch=False)
            self.assertEqual(git("rev-parse", "HEAD"), revision)
            self.assertEqual((root / ".git/index").read_bytes(), index_before)
            self.assertEqual((root / "src/LICENSE").read_text(), "Upstream license\n")
            self.assertEqual((root / "src/LICENSE").read_bytes(), b"Upstream license\n")
            self.assertTrue((root / "src/DEPS").read_text().endswith("git_dependencies = 'DEPS'\n"))
            self.assertFalse((root / "src/.git").exists())
            state = json.loads((root / "parallax/.build/import-state.json").read_text())
            self.assertEqual(state["revision"], revision)
            self.assertEqual(state["files"], 3)
            with self.assertRaisesRegex(RuntimeError, "refusing to overwrite"):
                importer.import_tree(root, pin, fetch=False)
            self.assertEqual((root / "src/chrome/VERSION").read_text(), "MAJOR=155\n")


if __name__ == "__main__":
    unittest.main()
