# The full Chromium snapshot is tracked by the outer Parallax repository.
# A null URL makes gclient sync dependencies without touching the source Git state.
solutions = [
    {
        "name": "src",
        "url": None,
        "managed": False,
        "deps_file": "DEPS",
        "custom_deps": {},
        "custom_hooks": [
            {
                "name": "lastchange",
                "pattern": ".",
                "action": [
                    "python3", "src/build/util/lastchange.py",
                    "-o", "src/build/util/LASTCHANGE", "--filter", "",
                ],
            },
            {
                "name": "lastchange_commit_position_cros",
                "pattern": ".",
                "condition": "checkout_chromeos",
                "action": [
                    "python3", "src/build/util/lastchange.py", "-m", "CHROMIUM",
                    "--commit-position-header", "src/build/util/LASTCHANGE_commit_position.h",
                    "--filter", "",
                ],
            },
        ],
        "custom_vars": {
            "checkout_configuration": "small",
            "checkout_pgo_profiles": False,
            "checkout_src_internal": False,
            "cros_boards": "",
            "cros_boards_with_qemu_images": "",
        },
    },
]
target_os = ["linux", "chromeos"]
target_os_only = True
