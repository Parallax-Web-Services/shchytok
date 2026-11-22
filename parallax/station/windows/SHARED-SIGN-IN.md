# Station shared application sign-in

Status: deployed and enabled on Nexus and Slipstream on 2026-11-15. Windows build and smoke checks pass. The live HTTPS handoff, secure session cookie, origin allowlist, anonymous access rejection, and unbound ticket rejection pass. Authenticated native Windows checks now pass for Quark and Continuum using both a separate profile and the existing saved browser profile. The real React app-opening path reaches both app pages with visible WebViews, no login documents, no external login handoffs, and no shell error notice.

Station's existing credential is a Slipstream Sanctum token. It cannot authenticate Quark, Continuum or Nexus directly. Default-browser cookies are not available to embedded WebView2 profiles.

The new flow reuses Nexus OAuth and the existing Station device-code exchange:

1. With Station sign-in enabled, `/auth/cli` upgrades the existing browser OAuth grant to include the explicit `station:launch` scope once. Existing Nexus browser authentication handles this authorization; Station does not ask each application to sign in.
2. Slipstream binds an encrypted, access-only Nexus grant to the newly exchanged device token. Provider credentials stay on the service. Rotating browser refresh tokens are never copied to devices. The grant is hidden from token serialization and removed with that device token.
3. Station only attempts the handoff when authenticated `/api/v1/me` advertises `station_sign_in.available=true` with the trusted Nexus origin. Older services, disabled deployments, devices without grants and expired grants use ordinary app navigation without probing the undeployed route. Before opening a registered first-party app, native Station visits Nexus in that workspace's WebView2 profile. Nexus returns a random challenge bound to that browser session. An existing Nexus session is reusable only for the expected subject.
4. Native Station asks Slipstream to exchange the challenge for a 30-second, one-use Nexus ticket. Nexus requires the explicit scope, a verified identity, and an enrolled confidential first-party OAuth client.
5. Native Station POSTs the ticket to Nexus in the same WebView. Nexus verifies the browser binding, current token/client, and expiry, regenerates the web session, then redirects to the registered application. Ordinary application OAuth and permissions still apply.

Concurrent native handoffs are serialized across Station windows. Nexus retains a browser binding across outstanding challenges; consuming one ticket does not invalidate another app launch. Station waits for completion navigation before releasing the next handoff. Trusted first-party `/login` redirects may complete silently through Nexus; any rendered authentication document stays hidden and opens in the default browser.

The ticket does not pass through React, URL parameters, navigation history or an app's shell bridge. Third-party websites receive no automatic sign-in. Profiles remain isolated by service/account/workspace. A newly prepared profile needs a still-valid access grant; if that grant expired, Station must reconnect once. Existing Nexus web sessions are reused. This implementation does not add a refresh-token delegation protocol.

## Coordinated deployment

Review these authentication changes before deploying. Deploy Nexus support first while disabled; then deploy Slipstream code and run the additive `2026_11_14_120000_add_station_grant_to_personal_access_tokens` migration. Do not apply unrelated pending migrations implicitly.

On Nexus, set `STATION_SSO_CLIENT_IDS` to the existing Slipstream confidential OAuth client ID, preserving any existing list. Its client must have no user owner, remain confidential and active, and permit the new `station:launch` scope if a scope allowlist is configured. Set `STATION_SSO_ENABLED=true` on Nexus, then Slipstream, and rebuild each service's configuration cache using its existing deployment process. Keep HTTPS and secure host-only session cookies enabled. Both services need a shared, lock-capable application cache within each service's deployment.

Reconnect Station once after rollout. Old device tokens cannot acquire a Nexus grant retroactively. Verify Quark and a second application open without their own login prompts; verify a third-party site, another account/workspace, revoked credentials, expired tickets and repeated tickets cannot reuse the handoff. Public live checks and authenticated Quark/Continuum Windows acceptance pass.

Rollback: disable `STATION_SSO_ENABLED` on both services and rebuild configuration caches. Keep the nullable grant column until retained credentials have been handled. Disabling issuance does not log out existing normal web sessions.

## Validation

- Nexus `StationSessionTest`: concurrent launches, ticket consumption, browser binding, replay, expiry, client enrollment, scope/identity checks, and target allowlist.
- Slipstream `StationContinuityTest`: encrypted per-device grant exchange, no provider credential exposure, scoped handoff, device isolation and logout revocation, alongside existing durable workstream/project tests.
- Windows smoke mode: native browser isolation and build verification. Production Nexus-to-application OAuth acceptance still requires deployment.

## Production release — 2026-11-15

- Nexus: `026bb1a7c22e3e123126f8a3d8dcaa1c11271e1a`, based on the existing live revision `7de9475`.
- Slipstream: `88b2e12eb6d26645fc2a84dc0cf4359612346458`, based on the existing live revision `b73cd32`.
- Both exact commits are published on `codex/station-shared-sign-in-20261115` in their existing GitLab repositories.
- Ran only the additive Station grant migration. Existing unrelated Nexus working-tree changes were preserved and compared after rollout.
- Enrolled only the existing confidential Slipstream OAuth client. Secure, host-only session cookies are enabled on both services.
- Slipstream mirrors its path-based OAuth dependency into `vendor`; the audited controller was updated there as well as in package source. Future dependency installs read the updated package source.
- Private rollback backups: `/root/station-sso-releases/20261115T170216Z` on the existing server. They contain environment backups and must never be published.
- Production-base local tests: Nexus Station session tests (6 tests, 41 assertions); Slipstream shared sign-in, CLI upgrade and OAuth status tests (8 tests, 54 assertions).

Station's app shortcuts are directly available in the sidebar, with approved artwork and optional Nexus visibility retained. Existing tabs and the detailed app grid remain available.

## Canceled app initialization recovery

A failed or canceled WebView2 environment task must not remain in the workspace environment cache. Station removes that task and disposes its unused controller. A canceled initialization retries once before navigation or authentication writes begin. A second cancellation produces an app-view initialization error with an explicit retry action; session-ticket writes are never automatically replayed.

Reopening a tab that reported an error or required authentication retries navigation, including the normal shared sign-in handoff. Existing healthy tabs retain their state.

The Windows smoke check deliberately cancels the first environment creation, then verifies exactly one retry, successful page rendering, and a single retained browser page. The opt-in native `--check-shared-sign-in <output-directory>` diagnostic uses the saved Slipstream device credential and existing browser profile, opens Quark and Continuum through the real React/native path, and records only navigation paths/statuses and native failure details. It does not export credentials, authorization queries, or page content. Run it only in the current user's Windows session; it creates a separate diagnostic window and suppresses external login launches.

## Sidebar and overlapping navigation verification

The initial Nexus handoff now uses the same navigation-ID-bound waiter as ticket completion. An unrelated older navigation's canceled completion must not settle the current request. A real WebView smoke check starts overlapping navigations and requires the requested document to finish successfully.

Sidebar app buttons show an immediate Opening state and suppress duplicate clicks while a request is pending. The authenticated diagnostic clicks those actual sidebar elements rather than calling the browser-opening helper directly.

After installation, Windows UI Automation invoked Quark and Continuum in the user's running Station process (PID 45228). It required both the visible browser address and a non-offscreen remote document: Quark at `/quark` (Quark Finance), then Continuum at `/dashboard` (Today). Both checks passed. Evidence: `.build/windows-station-evidence/running-station-sidebar.json` relative to `parallax`.

## Painted app surfaces

Navigation, DOM presence, and Windows UI Automation visibility were insufficient acceptance criteria: the previous HWND-hosted WebViews could report a visible loaded app while the shell's native surface covered its pixels. Both the shell and app controls now use the existing WebView2 SDK's `WebView2CompositionControl`, so WPF controls their composition order. The app remains a WPF host with the same WebView2 engines, separate app profiles, native services and credential boundaries.

The project targets `net8.0-windows10.0.19041.0` to include the Windows SDK projection required by this control (Windows 10 version 2004 or newer). A new app controller starts as a measured, noninteractive one-pixel `about:blank` visual so composition initialization can finish; it is hidden before app navigation begins and remains hidden until authentication-document checks complete.

The Windows regression now captures the complete WPF visual and checks that the fixture app's white pixels actually appear over the shell. Live Quark and Continuum checks also save full composed-window images, in addition to page-only captures and DOM metrics. Navigation-only and accessibility-only receipts do not prove that a page is painted.

Final installed-process verification: PID 60868. A DPI-aware desktop capture shows the Finance page painted inside the running Station window. See `quark-running-window.png` in the local Windows evidence directory.
