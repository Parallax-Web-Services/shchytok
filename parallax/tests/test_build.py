"""Workflow safety checks that do not download or compile Chromium."""

from __future__ import annotations

import importlib.util
import os
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import Mock, patch


TOOLS = Path(__file__).resolve().parents[1] / "tools"


def module(name: str):
    spec = importlib.util.spec_from_file_location(name, TOOLS / f"{name}.py")
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


build = module("build")
launch = module("launch")


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "src").mkdir()
        (self.root / "parallax/config").mkdir(parents=True)
        config = {
            "solutions": [{
                "name": "src", "url": None, "managed": False,
                "deps_file": "DEPS", "custom_deps": {},
                "custom_hooks": [build.LASTCHANGE_HOOK, build.LASTCHANGE_CROS_HOOK],
            }],
            "target_os": ["linux", "chromeos"],
            "target_os_only": True,
        }
        (self.root / ".gclient").write_text(
            "\n".join(f"{key} = {value!r}" for key, value in config.items()) + "\n"
        )

    def test_source_is_unmanaged_and_has_no_clone_url(self):
        build.validate_client(self.root)
        path = self.root / ".gclient"
        path.write_text(path.read_text().replace("'url': None", "'url': 'https://example.test/src.git'"))
        with self.assertRaisesRegex(build.BuildError, "url=None"):
            build.validate_client(self.root)

    def test_client_requires_both_linux_and_chromeos_targets(self):
        path = self.root / ".gclient"
        original = path.read_text()
        for targets in ("['linux']", "['chromeos']", "['linux', 'chromeos', 'android']"):
            with self.subTest(targets=targets):
                path.write_text(original.replace("['linux', 'chromeos']", targets))
                with self.assertRaisesRegex(build.BuildError, "Linux and ChromeOS"):
                    build.validate_client(self.root)
        path.write_text(original)
        build.validate_client(self.root)

    def test_client_rejects_board_sdks_images_and_private_dependencies(self):
        path = self.root / ".gclient"
        original = path.read_text()
        forbidden_vars = (
            ("cros_boards", "amd64-generic", "board SDK"),
            ("cros_boards_with_qemu_images", "amd64-generic", "board SDK"),
            ("checkout_src_internal", True, "public Chromium"),
            ("checkout_src_internal_infra", True, "public Chromium"),
            ("checkout_google_internal", True, "public Chromium"),
        )
        for key, value, error in forbidden_vars:
            with self.subTest(key=key):
                altered = original.replace(
                    "'custom_deps': {}", f"'custom_vars': {{{key!r}: {value!r}}}, 'custom_deps': {{}}"
                )
                path.write_text(altered)
                with self.assertRaisesRegex(build.BuildError, error):
                    build.validate_client(self.root)

    def test_chromeos_commit_position_hook_preserves_upstream_options(self):
        self.assertEqual(build.LASTCHANGE_CROS_HOOK, {
            "name": "lastchange_commit_position_cros",
            "pattern": ".",
            "condition": "checkout_chromeos",
            "action": [
                "python3", "src/build/util/lastchange.py", "-m", "CHROMIUM",
                "--commit-position-header", "src/build/util/LASTCHANGE_commit_position.h",
                "--filter", "",
            ],
        })

    def test_os_configuration_generates_native_ash_in_its_own_output(self):
        preset = (TOOLS.parent / "config/os-development.gn").read_text(encoding="utf-8")
        (self.root / "parallax/config/os-development.gn").write_text(preset)
        with patch.object(build, "verify_depot", return_value=Path("/pinned/depot")), \
                patch.object(build, "prepare_source"), \
                patch.object(build, "check_branding"), \
                patch.object(build, "run") as run:
            output = build.configure(self.root, {}, "os-development")
        self.assertEqual(output, self.root / "src/out/ParallaxOS")
        run.assert_called_once()
        self.assertEqual(run.call_args.args[0], [
            str(Path("/pinned/depot/gn")), "gen", str(Path("out/ParallaxOS")), "--check",
        ])
        arguments = (output / "args.gn").read_text()
        required = (
            'target_os = "chromeos"', "is_chromeos_device = false", "is_debug = true",
            "dcheck_always_on = true", "is_component_build = true", "use_remoteexec = false",
            "is_chrome_branded = false", "chrome_pgo_phase = 0", "proprietary_codecs = false",
        )
        for setting in required:
            self.assertIn(setting, arguments)

    def test_os_build_compiles_chrome_target_from_ash_output(self):
        output = self.root / "src/out/ParallaxOS"
        with patch.object(build, "configure", return_value=output) as configure, \
                patch.object(build, "verify_depot", return_value=Path("/pinned/depot")), \
                patch.object(build, "run") as run:
            build.build(self.root, {}, "os-development", 8)
        configure.assert_called_once_with(self.root, {}, "os-development")
        self.assertEqual(run.call_args.args[0], [
            str(Path("/pinned/depot/autoninja")), "-C", str(Path("out/ParallaxOS")), "chrome", "-j", "8",
        ])

    def test_os_launcher_isolates_ash_profile_and_enables_host_clipboard(self):
        binary = self.root / "src/out/ParallaxOS/chrome"
        binary.parent.mkdir(parents=True)
        binary.touch()
        with patch.dict(os.environ, {"XDG_CONFIG_HOME": str(self.root / "config")}):
            args = launch.launch_arguments(self.root, "os-development", ["about:blank"])
        self.assertEqual(args, [
            str(binary), f"--user-data-dir={self.root / 'config/parallaxos-development'}",
            "--class=parallaxos", "--use-system-clipboard", "about:blank",
        ])
        self.assertFalse(any(arg.startswith("--login-") for arg in args))
        with self.assertRaisesRegex(ValueError, "Sandbox"):
            launch.launch_arguments(self.root, "os-development", ["--no-sandbox"])

    def test_browser_release_and_development_launch_profiles_are_preserved(self):
        for configuration, output, profile in (
            ("release", "Parallax", "parallax"),
            ("development", "ParallaxDevelopment", "parallax-development"),
        ):
            with self.subTest(configuration=configuration):
                binary = self.root / "src/out" / output / "chrome"
                binary.parent.mkdir(parents=True)
                binary.touch()
                with patch.dict(os.environ, {"XDG_CONFIG_HOME": str(self.root / "config")}):
                    args = launch.launch_arguments(self.root, configuration, [])
                self.assertEqual(args, [
                    str(binary), f"--user-data-dir={self.root / 'config' / profile}", "--class=parallax",
                ])
    def test_lastchange_hook_uses_fork_head_and_keeps_upstream_output(self):
        self.assertEqual(build.LASTCHANGE_HOOK, {
            "name": "lastchange",
            "pattern": ".",
            "action": [
                "python3", "src/build/util/lastchange.py",
                "-o", "src/build/util/LASTCHANGE", "--filter", "",
            ],
        })
        client_path = self.root / ".gclient"
        client_path.write_text(client_path.read_text().replace("'custom_hooks'", "'missing_hooks'"))
        with self.assertRaisesRegex(build.BuildError, "scoped Parallax LASTCHANGE hook"):
            build.validate_client(self.root)
    def test_client_validation_never_executes_code(self):
        (self.root / ".gclient").write_text("raise RuntimeError('must not execute')\n")
        with self.assertRaises(build.BuildError):
            build.validate_client(self.root)

    def test_prepare_is_guarded_idempotent_and_read_only_when_checking(self):
        deps = self.root / "src/DEPS"
        original = "# upstream\ngit_dependencies = 'SYNC'\n"
        deps.write_text(original)
        with self.assertRaisesRegex(build.BuildError, "needs preparation"):
            build.prepare_source(self.root, check=True)
        self.assertEqual(deps.read_text(), original)
        build.prepare_source(self.root)
        prepared = deps.read_text()
        self.assertIn("git_dependencies = 'DEPS'", prepared)
        build.prepare_source(self.root)
        self.assertEqual(deps.read_text(), prepared)
        deps.write_text("git_dependencies = 'unexpected'\n")
        with self.assertRaisesRegex(build.BuildError, "Unrecognized"):
            build.prepare_source(self.root)
        self.assertEqual(deps.read_text(), "git_dependencies = 'unexpected'\n")

    def test_environment_pins_tools_and_suppresses_git_prompts(self):
        with patch.dict(os.environ, {"GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "example.key",
                                     "GIT_CONFIG_VALUE_0": "preserved"}, clear=True), \
                patch.object(build.sys, "platform", "linux"):
            env = build.command_environment(Path("/tools"))
        self.assertEqual(env["DEPOT_TOOLS_UPDATE"], "0")
        self.assertEqual(env["GIT_TERMINAL_PROMPT"], "0")
        self.assertEqual(env["GIT_CONFIG_VALUE_0"], "preserved")
        self.assertEqual(env["GIT_CONFIG_KEY_1"], "credential.helper")
        self.assertEqual(env["GIT_CONFIG_VALUE_1"], "")
        self.assertEqual(env["GIT_CONFIG_KEY_4"], "protocol.https.allow")
        self.assertEqual(env["GIT_CONFIG_KEY_5"], "core.autocrlf")
        self.assertEqual(env["GIT_CONFIG_VALUE_5"], "false")
        self.assertEqual(env["GIT_CONFIG_KEY_6"], "core.eol")
        self.assertEqual(env["GIT_CONFIG_VALUE_6"], "lf")
        self.assertEqual(env["GIT_CONFIG_KEY_7"], "core.symlinks")
        self.assertEqual(env["GIT_CONFIG_VALUE_7"], "true")
        self.assertEqual(env["GIT_CONFIG_COUNT"], "8")
        self.assertTrue(env["PATH"].startswith(str(Path("/tools"))))

    def test_git_symlink_checkout_setting_is_linux_only(self):
        with patch.dict(os.environ, {}, clear=True), \
                patch.object(build.sys, "platform", "win32"):
            env = build.command_environment()
        configured_keys = [
            value for key, value in env.items() if key.startswith("GIT_CONFIG_KEY_")
        ]
        self.assertIn("core.autocrlf", configured_keys)
        self.assertIn("core.eol", configured_keys)
        self.assertNotIn("core.symlinks", configured_keys)
    def test_preflight_rejects_low_disk_and_low_memory(self):
        enough_disk = Mock(free=build.MIN_FREE_BYTES)
        with patch.object(build.sys, "platform", "linux"), \
                patch.object(build.platform, "machine", return_value="x86_64"), \
                patch.object(build.shutil, "which", return_value="/usr/bin/git"), \
                patch.object(build.shutil, "disk_usage", return_value=Mock(free=build.MIN_FREE_BYTES - 1)):
            with self.assertRaisesRegex(build.BuildError, "100 GiB"):
                build.preflight(self.root)
        with patch.object(build.sys, "platform", "linux"), \
                patch.object(build.platform, "machine", return_value="x86_64"), \
                patch.object(build.shutil, "which", return_value="/usr/bin/git"), \
                patch.object(build.shutil, "disk_usage", return_value=enough_disk), \
                patch.object(build, "ram_bytes", return_value=build.MIN_RAM_BYTES):
            with self.assertRaisesRegex(build.BuildError, "more than 16 GiB"):
                build.preflight(self.root)

    def test_preflight_detects_source_version_drift(self):
        (self.root / "src/chrome").mkdir()
        (self.root / "src/build").mkdir()
        (self.root / "src/DEPS").write_text("git_dependencies = 'DEPS'\n")
        (self.root / "src/BUILD.gn").touch()
        (self.root / "src/build/install-build-deps.sh").touch()
        (self.root / "src/chrome/VERSION").write_text("MAJOR=154\nMINOR=0\nBUILD=1\nPATCH=2\n")
        with patch.object(build.sys, "platform", "linux"), \
                patch.object(build.platform, "machine", return_value="x86_64"), \
                patch.object(build.shutil, "which", return_value="/usr/bin/git"), \
                patch.object(build.shutil, "disk_usage", return_value=Mock(free=build.MIN_FREE_BYTES)), \
                patch.object(build, "ram_bytes", return_value=build.MIN_RAM_BYTES + 1), \
                patch.object(build, "load_manifest", return_value={"chromium": {"tag": "155.0.8059.84"}}):
            with self.assertRaisesRegex(build.BuildError, "differs from upstream.json"):
                build.preflight(self.root)
    def test_bootstrap_initializes_new_and_existing_pinned_tool_runtime(self):
        depot = build.depot_path(self.root)
        manifest = {"depot_tools": {
            "repository": build.DEPOT_REPOSITORY,
            "revision": "a" * 40,
        }}
        for existing in (False, True):
            with self.subTest(existing=existing):
                events = []

                def fake_git(args, **kwargs):
                    events.append(("git", args))
                    if args[0] == "clone":
                        depot.mkdir(parents=True)

                def fake_verify(root, pin):
                    events.append(("verify", pin))
                    return depot

                def fake_run(args, **kwargs):
                    events.append(("runtime", args, kwargs))
                    (depot / "python3_bin_reldir.txt").write_text("bootstrap/python3/bin")

                with patch.object(build, "git", side_effect=fake_git), \
                        patch.object(build, "verify_depot", side_effect=fake_verify), \
                        patch.object(build, "run", side_effect=fake_run):
                    build.bootstrap(self.root, manifest)

                expected_git = [] if existing else [
                    ["clone", "--no-checkout", "--depth=1", build.DEPOT_REPOSITORY, str(depot)],
                    ["fetch", "--depth=1", "origin", "a" * 40],
                    ["checkout", "--detach", "a" * 40],
                ]
                self.assertEqual([event[1] for event in events if event[0] == "git"], expected_git)
                runtime = next(event for event in events if event[0] == "runtime")
                self.assertEqual(runtime[1], [str(depot / "ensure_bootstrap")])
                self.assertEqual(runtime[2]["cwd"], depot)
                self.assertEqual(runtime[2]["env"]["DEPOT_TOOLS_UPDATE"], "0")
                self.assertEqual(runtime[2]["env"]["DEPOT_TOOLS_BOOTSTRAP_PYTHON3"], "1")
                self.assertEqual(runtime[2]["env"]["DEPOT_TOOLS_DIR"], str(depot))
                self.assertTrue(runtime[2]["env"]["PATH"].startswith(str(depot)))
                self.assertEqual([event[0] for event in events[-3:]], ["verify", "runtime", "verify"])

    def test_bootstrap_detects_missing_python_runtime_after_bootstrap(self):
        depot = build.depot_path(self.root)
        depot.mkdir(parents=True)
        with patch.object(build, "verify_depot", return_value=depot), \
                patch.object(build, "run"):
            with self.assertRaisesRegex(build.BuildError, "Python runtime was not initialized"):
                build.bootstrap(self.root, {})
    def test_sync_does_not_update_the_source_solution_or_force_dependency_edits(self):
        calls = []
        with patch.object(build, "verify_depot", return_value=Path("/pinned/depot")), \
                patch.object(build, "prepare_source"), \
                patch.object(build, "run", side_effect=lambda args, **kwargs: calls.append((args, kwargs))):
            build.sync(self.root, {})
        self.assertEqual(calls[0][0], [str(Path("/pinned/depot/gclient")), "sync", "--nohooks", "--no-history"])
        self.assertEqual(calls[1][0], [str(Path("/pinned/depot/gclient")), "runhooks"])
        self.assertTrue(all(call[1]["cwd"] == self.root for call in calls))

    def test_configure_checks_branding_before_gn_and_preserves_custom_arguments(self):
        (self.root / "parallax/config/release.gn").write_text("is_debug = false\n")
        calls = []
        with patch.object(build, "verify_depot", return_value=Path("/pinned/depot")), \
                patch.object(build, "prepare_source"), \
                patch.object(build, "check_branding", side_effect=lambda root: calls.append("branding")), \
                patch.object(build, "run", side_effect=lambda args, **kwargs: calls.append(args)):
            output = build.configure(self.root, {}, "release")
            self.assertEqual(calls[0], "branding")
            self.assertEqual(calls[1], [str(Path("/pinned/depot/gn")), "gen", str(Path("out/Parallax")), "--check"])
            (output / "args.gn").write_text("my_custom_args = true\n")
            with self.assertRaisesRegex(build.BuildError, "different arguments"):
                build.configure(self.root, {}, "release")
            self.assertEqual((output / "args.gn").read_text(), "my_custom_args = true\n")

    def test_launcher_uses_a_separate_profile_and_preserves_urls(self):
        binary = self.root / "src/out/Parallax/chrome"
        binary.parent.mkdir(parents=True)
        binary.touch()
        with patch.dict(os.environ, {"XDG_CONFIG_HOME": str(self.root / "config")}):
            args = launch.launch_arguments(self.root, "release", ["https://example.test/a?x=1&y=2"])
        self.assertEqual(args[0], str(binary))
        self.assertIn(f"--user-data-dir={self.root / 'config/parallax'}", args)
        self.assertEqual(args[-1], "https://example.test/a?x=1&y=2")
        for flag in launch.UNSAFE_FLAGS:
            with self.assertRaisesRegex(ValueError, "Sandbox"):
                launch.launch_arguments(self.root, "release", [flag])
            with self.assertRaisesRegex(ValueError, "Sandbox"):
                launch.launch_arguments(self.root, "release", [f"{flag}=true"])

    def test_launcher_allows_an_explicit_profile_without_duplicate_flags(self):
        binary = self.root / "src/out/ParallaxDevelopment/chrome"
        binary.parent.mkdir(parents=True)
        binary.touch()
        args = launch.launch_arguments(self.root, "development", ["--user-data-dir=/tmp/parallax-test"])
        self.assertEqual(sum(arg.startswith("--user-data-dir") for arg in args), 1)


if __name__ == "__main__":
    unittest.main()