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
        ],
        "custom_vars": {
            "checkout_configuration": "small",
            "checkout_pgo_profiles": False,
            "checkout_src_internal": False,
        },
    },
]
target_os = ["linux"]
target_os_only = True
