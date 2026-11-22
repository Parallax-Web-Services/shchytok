#!/usr/bin/env python3
"""Run inside the pinned developer preview to open its upstream guest session.

Uses the image's own test library. No personal account is created or signed in.
As with upstream autologin.py, deliberately leaves the browser session running.
"""

import logging
import sys

sys.path.insert(0, "/usr/local/autotest/bin")
import common  # noqa: F401 -- upstream test-image import setup
from autotest_lib.client.common_lib.cros import chrome
from telemetry.internal.backends.chrome import oobe

logging.getLogger().setLevel(logging.WARNING)

# This pinned test image can expose Oobe.readyForTesting before its optional
# OobeAPI JavaScript wrapper exists. Use the same upstream test-only WebUI
# message as that wrapper; the normal guest-launch backend still waits for
# Chrome to restart and rebinds its DevTools connection.
original_guest_login = oobe.Oobe.NavigateGuestLogin


def navigate_guest_login(contents):
    contents.WaitForJavaScriptCondition(
        "typeof Oobe == 'function' && Oobe.readyForTesting", timeout=60)
    if contents.EvaluateJavaScript(
            "typeof window.OobeAPI === 'object' && "
            "typeof window.OobeAPI.loginAsGuest === 'function'"):
        return original_guest_login(contents)
    contents.ExecuteJavaScript("chrome.send('OobeTestApi.loginAsGuest')")


oobe.Oobe.NavigateGuestLogin = navigate_guest_login
session = chrome.Chrome(logged_in=False, num_tries=1,
                        extra_browser_args=["--no-startup-window", "--enable-oobe-test-api"])
tab = session.browser.tabs.New(in_new_window=True)
tab.Navigate("chrome://version")
tab.WaitForDocumentReadyStateToBeComplete(timeout=30)
print("Parallax preview guest session ready: chrome://version", flush=True)
