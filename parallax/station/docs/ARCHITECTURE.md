# Station product architecture and first vertical slice

The product directive governs Station: Parallax is the encompassing business operations platform; Station is its cross-platform client shell; Helm provides composable management surfaces; Continuum coordinates; Slipstream executes persistent workstreams; Plexus connects people, systems and resources; Positronic owns durable knowledge and Context Kits; Quark owns finance with Parity beneath it; Freighter publishes signed packages; Archer resolves and installs; Nucleus bootstraps infrastructure. Product URLs alone are not integration contracts.

## Audited implementation and authoritative contracts

| Layer | Existing source | Reuse and boundary |
| --- | --- | --- |
| Windows shell | `station/windows/MainWindow*`, `BrowserTab`, `FilesTab`, `TerminalTab`, `PtySession`, `NativePanels` | Preserve WPF/WebView2/ConPTY, session tabs, native previews and telemetry. These are client presentation and explicit local capabilities, not cloud workstream state. |
| Identity / membership | `nexus/routes/api.php` (`/api/me`, `/api/v1/identity`); Slipstream `Api/V1/AuthController`, `User::authPayload`, `SetApiTeam` | Nexus remains authoritative. Consume Slipstream's existing authorized mirror through `/api/v1/me`; never create organizations locally. `X-Slipstream-Team` scopes requests. |
| Device authorization | Slipstream `/auth/cli` and `/api/v1/auth/exchange` | Existing human sign-in issues a 60-second one-use code. Exchange it for a per-device Sanctum token. No passwords, scraped cookies, service tokens, or worker credentials. Windows stores the token in Credential Manager. |
| Organization context | `parallax-app/CurrentTeam`, `NexusUserMapper`, `SyncNexusTeams`; Slipstream `SetApiTeam` | Stable organization reference is service origin + authenticated user ID + authorized team ID. Local recency and pins are preferences; membership is revalidated from the service. |
| Operational home | Slipstream `Deck::payload`, `/api/v1/dashboard`, `/api/v1/workstreams` | Consume real attention/activity/workstreams; preserve empty and disconnected states. Do not synthesize agents, workloads or approval counts. |
| Execution / conversation | `WorkstreamActions::viewPayload/askAgent`, `/api/v1/workstreams/{id}`, `/agent` | Reuse projects/repositories, worker events, tasks, cycles and conversations. Agent turns are durable queued cycles dispatched by Slipstream. Closing Station never sends a cancellation. |
| Decisions | `TaskActions`, `/api/v1/tasks/{id}/approve|reject` | Display proposal details, require a deliberate human confirmation, submit once, refetch authoritative state. No automatic retry of writes or elevated execution grants. |
| Cross-device | Same Slipstream v1 controllers used by mobile; `WorkstreamsController` web views | Stable server workstream ID; web route `/workstream/{id}`. Native recency/seen markers survive restart; operational records stay on Slipstream and are accessible to other authorized clients. |
| Shared management surfaces | `helm/README.md`, `continuum/docs/workspace-navigation.md`, `continuum/routes/executive-workspaces.php` | Helm is reusable UI; Continuum already bridges Quark/Pulsar using server-side Nexus tokens and authorized team scope. Do not duplicate these bridges or expose their credentials to embedded pages. |
| Ecosystem registry | `parallax-app/config/suite.php`, `app/Suite`, `/api/suite`; Slipstream `config/workspaces.php` | Existing product registry remains authoritative. Station's artwork catalog is packaging metadata, not permission to access a capability. Native contribution IDs are separate from product URLs. |
| Installation / distribution | `archer/docs/PROTOCOL.md`, `DESIGN.md`; Nucleus fabric/hostedworker | Reuse Archer's approved install/preflight and native project definitions in a later integration. Freighter signing/registry contract was not found in the audited Station-facing sources; do not invent a replacement registry. |

## Current gaps and smallest change

Before this slice, workspace was a URL in settings, tabs were the only local state, Home was a static launcher, and no service execution or agent contract was consumed. A URL must not become organization identity. Add a client context/service adapter and an atomic local navigation journal; replace only Home's contents; contribute a native Slipstream workstream surface. Preserve approved art and all native tools.

The first slice uses **Slipstream's existing v1 API**, not a new Station backend. Workspace discovery and switching come from authorized teams. Context references qualify every workstream/resource with service, account, team and capability. Local pins, recency and seen event IDs persist independently of tabs. API reads are fresh; membership/authorization failures do not fall back to cached operational claims. Windows Credential Manager stores the device credential separately from navigation state; native browser profiles are isolated by context. Independently trusted web content has no agent/terminal bridge.

The home command input initially supports permitted resource search, workstream navigation, and explicit native commands. Continuum conversation and creation commands must remain visibly unavailable until a real compatible contract is integrated. Native terminal targets are explicitly local WSL/Command Prompt; a workstream association is descriptive and does not authorize remote execution. Do not open a remote terminal by interpreting an API hostname or repository path as executable input.

## Validation and acceptance limits

Exercise the actual Slipstream HTTP controllers and database with isolated test data: authorize two device sessions, retain a workstream/cycle after logout, reconnect from the other token, reject foreign resources and unauthorized teams, and retain pending human decisions. Native checks cover context persistence, qualified IDs, workspace tab isolation, local terminal behavior, and the real API adapter shape. Fixtures belong only to tests and never appear as operational data.

A local contract test is not a live cloud execution demonstration. The full defining demonstration additionally needs a signed-in account, an existing authorized workstream, a functioning Slipstream queue/executor, and a phone using the same workspace. No deployment, live agent direction, task approval, remote shell, or credential access is implied by building and testing the client. Workspace visual themes remain default until authoritative organization theme data is available. Full distributed resource discovery, Continuum conversation, remote execution, notifications, and Freighter installation remain separate capability integrations.

## Subsequent product corrections

The standalone Files feature was removed at the owner's direction: no dock entry, command, file-manager class or shell preview route remains. Workstream resource references and repository inspection remain contextual. **Connect Slipstream** opens device sign-in in the operating system's default browser; Station retains the one-time-code exchange surface. Browser sign-in sessions are not scraped or copied into Station.

The owner subsequently rejected switchers as the Station presentation. App navigation now uses a persistent grid tab with visible columns, product details and tools, and organization contexts use visible cards. Core's shared catalog/component support an opt-in inline presentation; existing web consumers keep their current dialog UI.

Organization navigation subsequently moved to a Codex-like folder hierarchy: Organization → Project → Task → Subtask, with project details alongside it. This uses existing Slipstream projects/tasks and adds a nullable parent-task relation in the shared task table, not a separate Station task database. Workstreams remain durable execution contexts linked to projects/tasks. New task creation records proposed work and never dispatches execution. The app catalog remains a separate persistent grid.

## React presentation, 14 November 2026

The owner approved replacing the WPF product interface with a web-rendered shell while preserving the native foundation. Station now packages a React shell using Helm's real `AppShell` and design tokens and Core's inline app grid. `StationRuntime` coordinates the existing Slipstream client, journal, Windows Credential Manager and ConPTY implementation. `StationWebHost` exposes an origin-bound, method-limited capability bridge only to the packaged shell. Remote pages are separate WebViews with organization-scoped profiles and no native shell bridge. All navigation, project/task hierarchy, tabs, forms, approvals and terminal chrome now render in React. The retired WPF presentation is excluded from the build; its development copy is preserved under `.build/retired-wpf-presentation`.

The Windows window and executable retain the original Parallax orbital logo, with added taskbar DPI frames. Organization rows use business icons. Terminals use Microsoft Terminal-inspired neutral chrome and a visible WSL/Windows toggle, selecting an existing profile session before creating another. Native process identity and working directories are preserved. This does not embed Microsoft Terminal itself or introduce an Electron runtime.

Per-app local PHP server packaging remains a subsequent integration; the React shell itself is packaged static content served by WebView2's local virtual-host mapping. No local PHP app server is claimed by this change.
