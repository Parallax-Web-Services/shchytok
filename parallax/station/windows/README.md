# Parallax Station for Windows

Station is a native Windows host with a **locally rendered React interface**. Helm supplies the application frame and design tokens; Core supplies the persistent app grid, catalog and product artwork. The visible navigation tree, tabs, Home, forms, project/task details, workstream conversation, approvals, machine view and terminal chrome are web-rendered. WPF supplies the window and WebView2 hosting, with the existing native backend underneath. No Electron runtime or external browser is needed for the shell.

## Open and connect

Open `D:\Parallax\StationWindows\app\Parallax.Station.exe`. Use **Connect** and **Open sign-in in browser** to complete the existing Nexus-backed Slipstream device flow in the default browser. Paste the 60-second one-use code into Station. Native code exchanges it and stores the device token in Windows Credential Manager. Tokens and browser cookies are never exposed to the React application or remote product pages.

Authorized organizations appear as business folders in the sidebar. Expand them to see projects, tasks and nested subtasks. Organization names are displayed without repeating the service address. Project/task details open in tabs; workstreams retain stable server identity independent of those tabs. **Apps** is a persistent grid, not a popup switcher. There is no standalone Files feature.

Home consumes real Slipstream workstreams, attention items and execution state. Empty/disconnected states contain no simulated operational activity. Search filters permitted workstreams; commands include `/terminal`, `/machine`, `/connect`, and `/workstream <id>`. Continuum conversation is not connected yet.

Pins, recency, reviewed-event markers and organization selection use the existing native workspace journal at `%LOCALAPPDATA%\Parallax\Station\workspace-context.json`. Expanded tree preferences are stored in the local shell profile. Operational records remain authoritative in Slipstream. Local preferences are not a shared cross-device database.

## Terminal and native services

The terminal uses the existing Windows **ConPTY** backend with local xterm rendering inside React. Its compact session tabs and neutral dark chrome follow Microsoft Terminal's visual language. Cascadia Mono is used when installed, with Consolas as fallback.

**WSL is the default.** The profile selector offers Ubuntu (WSL), PowerShell and Command Prompt. PowerShell uses the installed stable PowerShell 7 when available, falling back to Windows PowerShell. The visible WSL toggle switches between the existing WSL session and the last selected Windows profile, creating a session if necessary. It preserves each session's process, working directory and output. **New terminal** creates another independent session using the selected profile. The profile selector and WSL toggle describe the selected session. The WSL profile uses the Ubuntu logo, aubergine background and Tango-style ANSI colors; Windows retains its separate dark profile. `Ctrl+Shift+T` creates a terminal; `Ctrl+Shift+W` closes the selected session. `Ctrl+Shift+C/V` copy/paste. Ordinary Ctrl+C still interrupts the foreground process when no text is selected. Restart explicitly starts a fresh shell in that session.

Normal WSL sessions use the default distribution and shell, starting at home. The WSL test shell runs clean Bash without writing user history; isolated PowerShell checks skip user profiles and disable PSReadLine history persistence. Normal PowerShell sessions retain the user’s own profile and preferences. Closing a terminal ends only its ConPTY process group; closing Station ends its local terminals. It never calls `wsl --shutdown`. Local terminals do not represent cloud execution.

Machine displays real Windows CPU/memory and device identity. Credential storage, shell processes, clipboard access and OS integration remain native. No remote execution target is inferred from a project or repository.

## Shared app switcher

Click the Parallax logo in the sidebar header to open the same Core app switcher used on the web. Visible sidebar app shortcuts remain available. The shared switcher supports search and keyboard dismissal; selecting a capability opens its Station surface or follows the configured browser preference. Nexus stays opt-in.

The Core component's optional `onOpenChange` callback lets Station temporarily hide embedded browser surfaces while the dialog is open and restore the active surface when it closes. No remote app gains a native bridge. Sidebar shortcuts do not use native title tooltips for executive roles.

## Browser and security boundaries

Remote product pages use **separate WebView2 controls**, positioned beneath React's browser toolbar and within its tab content region. Profiles are isolated by service/account/organization identity. Back/forward/reload/address controls are React; native code performs navigation. Popups receive a child surface in the same browser environment and are routed into Station tabs.

Only the packaged local shell at `https://station-shell.invalid/index.html` receives the native RPC bridge. Remote pages have no terminal, credential or OS bridge. The shell's CSP prohibits network connections, remote scripts, frames and objects; its assets, fonts and terminal renderer ship locally. Native RPC exposes specific methods, not an arbitrary API proxy or command launcher. Resource methods check the active organization, and the service still enforces account/team/resource permissions. Browser and terminal resources retain their context when hidden.

Task decisions require a React confirmation backed by a short-lived, one-use native decision challenge. Elevated task approval is delegated to Slipstream's existing review flow. Approval may execute a configured cloud action immediately; writes are not retried automatically.

`Ctrl+K` focuses workspace search; Ctrl+W closes a surface outside terminal input. F11 toggles the native window's fullscreen mode. Terminal Ctrl+W remains terminal input.

## Build and validation

```bash
node shchytok/parallax/tools/build_station_shell.mjs
```

The build reuses the installed Slipstream React/esbuild dependencies and Helm's Tailwind toolchain. It bundles **one React runtime**, the actual Helm frame, Core's inline grid, original artwork and terminal assets. Source hashes are recorded in `Assets/Shell/provenance.json`; bundled dependency versions/licenses are under `ThirdParty/Shell`.

Stage this directory on Windows, then run:

```powershell
& '.\build.ps1' -Restart -SmokeTest -Run
```

The development build uses .NET 8 Windows desktop targeting Windows 10 version 2004 (build 19041) or newer, Evergreen WebView2 and the pinned WebView2 SDK. Shell and app surfaces use WebView2CompositionControl so WPF can compose app pixels above the shell. It is a framework-dependent executable, not yet an installer. Production runtime support, signing, updates and installation remain release work.

Smoke mode exercises the **actual React UI and native RPC bridge**: Core grid/search/tab preservation; real WSL/CMD execution; explicit WSL toggle; independent sessions; working-directory retention; no-echo input; Ctrl+C; resize; cleanup; project/subtask/workstream API response rendering; human decision dialog; telemetry; browser popup routing; and denial of remote-page access to the shell bridge. A second process checks journal persistence. Evidence is written to `D:\Parallax\StationWindows\evidence` with isolated profiles and synthetic contract data; fixtures never appear in normal operation.

The shared Slipstream suite separately tests real controllers/domain services with isolated SQLite. Its prepared project/subtask migration and API changes must be deployed before live hierarchy creation is available. The full real-account/cloud-executor/phone demonstration remains a live acceptance step, not a claim made by local UI tests.

## App packaging and artwork

Per-app local PHP application-server packaging is the intended next integration layer; this UI change does not claim those servers are installed. Existing authoritative service contracts remain in use.

Approved Parallax/product artwork is preserved. Windows uses the approved Parallax orbital logo in both the taskbar and window icon, with explicit 16–256 px icon frames and a full-resolution window image. The same orbital mark and wordmark remain inside Station; the business symbol is reserved for organization rows in the navigation tree. Original source artwork and provenance remain packaged.


Project expansion now fetches tasks independently of the selected details tab or organization. Restored open folders also load their data. Failed loads show an inline error and Retry rather than an instruction to open the project. Older Slipstream deployments without the project-details endpoint use their authorized project/workstream APIs to expose existing tasks read-only. This compatibility path does not invent subtask links or enable task creation; those operations still require the deployed hierarchy API. The native bridge exposes a separate authorized read operation for tree expansion, while mutations retain active-organization checks.

## Moving and closing tabs

Drag a tab out of the tab strip to open it in its own Station window, or use its move-to-window button. Terminal processes, screen history and working directories move with their tab. Browser tabs move their actual WebView, retaining page state. The destination owns moved resources, so closing the original window does not dispose them. Middle-click closes a tab and cleans up its owned resources; Home remains available.

The top navigation shows the active app's registered icon and name. Shared sign-in implementation and deployment requirements are documented in [SHARED-SIGN-IN.md](SHARED-SIGN-IN.md).


## Login routing and optional administration

Login, registration and authorization routes open in the system default browser. Redirected login pages are cancelled before display, and unauthorized responses or credential forms keep the embedded surface hidden while handing off to the browser. The tab offers Open in browser and an explicit retry. This does not copy a browser session back into Station; shared sessions still require the server integration described above.

Nexus is hidden from Apps by default. Connected users managing their own SaaS can enable **Settings → Show Nexus**. This is an account-scoped presentation preference; it grants no permissions. Other windows receive the updated preference.

Unravel is registered at `https://unravel.tools/dashboard` with its existing signal-and-strands mark. Its project URLs use the normal isolated browser tab lifecycle and app identity. See [UNRAVEL.md](UNRAVEL.md) for the limits of this integration.


## Browser preferences and returning pages

Settings → Browser behavior chooses whether ordinary web links open in Station tabs or the Windows default browser. Sign-in remains external. The browser toolbar can send the current page outward, and Settings can open Windows default-app settings. HTTPS addresses pasted into the universal input or the Settings address field can be opened explicitly inside Station.

Users can explicitly enable the per-user `parallax-station://open?url=...` handler from Settings, then copy the Send to Station bookmark into their browser. It sends only the encoded HTTPS page address. The handler rejects credentials, other schemes, extra actions and oversized payloads. A same-user/session named pipe forwards the address to an existing Station process and waits for its workspace bootstrap before opening a tab. Registration is user-triggered; smoke tests do not modify the user's Windows registry.

Slipstream's primary app entry now opens its API-backed workstream surface using the existing Station connection, not another embedded dashboard login. Its full website and other web-only resources keep their own session requirements.

## App-specific browser sign-in

The native Unravel adapter prepares a non-login page in the target WebView, validates same-origin metadata, opens authorization externally, receives one state-bound callback on a random loopback port, and redeems the code plus S256 verifier and ordinary CSRF token inside the same WebView. It preserves the requested app path. The verifier never enters a URL or page bridge. Closing a tab/window cancels the listener; unsupported or disabled server contracts retain a truthful pending tab and ordinary external login.

The adapter uses the server's explicitly supported `/station/callback` compatibility path; `/station/auth/callback` is the server's canonical alternative. Native accepts exactly its own generated callback address. The matching Unravel contract is documented in `unravel/docs/station-auth.md`; those server changes remain undeployed/disabled until approved. No Nexus trust is added for Unravel. This is separate from the pending Nexus/Slipstream shared-auth rollout.

Checks exercise real same-user pipe transport, actual loopback HTTP callbacks, PKCE/state/host validation, browser preferences, direct/redirected credential screens, connected Slipstream, PowerShell state/popout, and context restart. Live Unravel sign-in acceptance requires the matching server endpoint to be enabled.


## Start menu and startup recovery

`register-start-menu.ps1` creates/verifies the current user's Programs → Parallax → Parallax Station shortcut, pointing at the built executable and its Parallax icon. No administrator permission or machine-wide shortcut is needed.

Startup retries the read-only identity lookup once after a transport failure. If it still fails, the shell keeps the saved device credential and offers an offline state plus Retry connection; local tools remain available. Authentication/permission errors remain distinct. Writes are never automatically retried. Transport messages include the service host and bounded inner diagnostic context, with URI credentials redacted. Certificate validation stays at the platform default.

Native smoke checks simulate two TLS startup failures followed by recovery through the existing credential, and separately verify that a failed write is attempted only once. A Windows/.NET diagnostic also verified valid certificates and a successful authenticated read through the actual saved Slipstream client; the initially reported TLS failure was not reproducible at that check.

## Display scaling

The executable declares Per-Monitor V2 DPI awareness. Its WPF window and browser surfaces use layout rounding, and embedded browser bounds snap to physical pixel edges. This keeps WebView2's captured frame at the displayed resolution when moving between screens. The rendering checks compare both the Chromium capture and the actual composition frame with display dimensions at each available monitor's scale; the current Windows environment passes at 100% and 125%.

WebView2's built-in link status bar is disabled in the packaged shell and embedded app controls, so hovering switcher links does not draw browser URL chrome over Station.

## Infrastructure and tab identity

Infrastructure replaces the Machine navigation item and contains local hardware telemetry, terminal access, and the existing Nebula and Nucleus management surfaces. Nebula and Nucleus are infrastructure capabilities and are excluded from the main app shortcuts and shared app switcher. Remote inventory remains managed in those services; Station does not invent or aggregate resource status here. The native `machine` telemetry contract and `/machine` command alias remain compatible; `/infrastructure` opens the same view.

Browser tabs receive real favicon changes from WebView2. The native host obtains the browser-cached favicon as PNG, bounds it to 64 KiB, and sends an image data URI to the shell. It does not forward a remote favicon URL or device credentials into the shell. Stale results are discarded after navigation or tab close; updates follow the tab’s native owner when it moves to another window. Approved product artwork is the fallback for integrated apps; local tools retain their own icons. Active tabs scroll into view as their title changes.
