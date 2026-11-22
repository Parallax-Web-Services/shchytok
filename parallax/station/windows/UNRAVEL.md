# Unravel in Station

Station's app catalog opens the real deployed application at `https://unravel.tools/dashboard`. Project arrangement and studio URLs retain the same Unravel app identity, workspace-isolated WebView profile, tab controls, and preserved page state when moved into a separate window. The packaged mark is derived from Unravel's existing `ApplicationLogo.tsx`; its geometry and colors are preserved, with provenance alongside the SVG.

Authentication remains required. Login routes open in the default browser under Station's normal policy; Unravel is not added to the Nexus handoff allowlist without an actual compatible service contract. This shell registration does not deploy the Unravel repository's uncommitted Maestro, arrangement or Kontakt changes and does not establish shared authentication.

The independent Unravel native worker has a verified Windows Kontakt render path. Station does not yet connect project performances to it. There is no instrument-render/project job API in the current read-only recordings contract. No native HTTP listener, public workstation port, remote-page OS bridge, REAPER dependency or executable-path selection has been introduced.

The next integration must reuse authorized project identity and the private outbound worker/lease infrastructure, with a reviewed instrument-render capability. It must bind project/track revisions and preset hashes, preserve editable MIDI, enforce deadlines and cancellation, reject stale results, and return verified audio as a private source. Cloud editing and previously completed audio must remain usable if a worker is offline. This requires coordinated app and worker changes; a launcher entry is not evidence that rendering works inside Station.

Validation covers the real Core app-grid entry, packaged original logo, canonical HTTPS dashboard link, app identity for arrangement routes, and Station's browser isolation/tab lifecycle. An authenticated edit → Kontakt render → playback → mix export acceptance test remains pending the job bridge and deployment of the appropriate Unravel build.
