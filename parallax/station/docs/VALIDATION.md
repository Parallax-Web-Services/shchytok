# First workspace / Slipstream slice: validation

Verified locally on 13 November 2026. Source audit and remaining acceptance boundaries are in `ARCHITECTURE.md`; operating/build instructions are in `../windows/README.md`.

- Windows Release publish: passed without compiler warnings. The existing WPF shell, approved artwork, native tools, browser popup routing, WSL/CMD terminal sessions and cleanup checks remain passing.
- Workspace checks: service/account/team-qualified resource identity, organization tab isolation, preserved local resource surfaces, team header and bearer scoping, foreign-origin rejection, unauthorized-response rejection, and no automatic write replay passed.
- Process restart: a separate Station process read the journal created by the smoke-test process and verified workspace selection, stable workstream reference, pin, and reviewed event ID. `context-restart.json` records that second process's result.
- Slipstream API continuity: `php8.5 vendor/bin/phpunit tests/Feature/StationContinuityTest.php` passed **1 integration test, 25 assertions**. This uses the real controllers/domain services, Sanctum tokens and an isolated in-memory SQLite database, with job dispatch intercepted. It covers one-use authentication, scoped workstream and dashboard reads, foreign-user rejection, durable queued direction, token revocation, second-device resume, unauthorized-team rejection and human task rejection.
- Native service surface: the synthetic response exported by that actual API integration test was passed to the native adapter in smoke mode. The surface rendered its queued cycle, pending task and persisted conversation. `service-payload-render.json` records this contract/render check. Test fixtures are never served as operational Home data.

Evidence is in `D:\Parallax\StationWindows\evidence`, with selected copies in `parallax/.build/windows-station-evidence` (build artifacts, not authoritative business records).

This does **not** verify a deployed account session, functioning production queue/worker, actual continued cloud execution, or a real phone interaction. The defining demonstration remains a live acceptance step: connect an authorized account in Station, resume a real workstream, direct its agent, inspect its project and explicit execution scope, close Station, and resume the same server workstream on the authorized phone. Local terminal processes intentionally end with Station and are not represented as cloud execution.

The local tests do not grant production approvals, run remote commands, deploy services, or modify user credentials. Windows Credential Manager is used only when the user explicitly connects a device; smoke tests use isolated synthetic credentials in an HTTP contract handler.

## Hierarchy extension

The expanded isolated Slipstream integration suite passes **2 tests, 47 assertions**: organization-scoped project listing, project tasks, nested subtasks, separate-device reads, cross-project/organization/owner rejection, forced proposed status, and no dispatched execution. Windows smoke checks consume the actual exported project response, expand Organization → Project → Parent task → Child task → Nested subtask, select the nested task, and verify the native project surface opens. Evidence: `project-hierarchy.png` and `project-hierarchy-render.json`. The new shared-service migration and API code are prepared locally; production deployment remains outstanding.

## React shell and native bridge, 14 November 2026

Windows Release publish and the integrated React-shell smoke checks pass. The checks now exercise the actual React interface rather than WPF views: Helm frame, persistent Core grid and search, retained tab state, project/nested-task selection, service-contract workstream conversation, human decision dialog, and native machine telemetry. Actual ConPTY/WSL checks cover Linux identity, real TTY, no-echo synthetic secret input, Ctrl+C, resize, independent processes, retained working directory, and cleanup. The WSL toggle is exercised against Command Prompt, then returns to the existing WSL session without creating another process. Remote browser popups are routed into isolated app surfaces; a remote page's attempted native terminal message cannot reach the shell bridge. A separate process verifies native journal persistence.

Current evidence is `shell-home.png`, `shell-apps.png`, `shell-project.png`, `shell-workstream.png`, `shell-terminal.png`, `shell-machine.png`, `shell-browser-content.png`, `acceptance.json`, and `context-restart.json` under the Windows evidence folder and selected workspace `.build` copies. Service views use clearly isolated API contract fixtures; normal operation has no fixture provider. No production execution, deployment or real-phone acceptance is implied by these local checks. No performance benchmark has been claimed.

The latest integrated checks additionally verify Ubuntu profile artwork and aubergine styling, expansion loading tasks without opening the project details tab, and read-only retrieval of existing workstream tasks when the project-details endpoint is unavailable. An unauthorized organization is rejected by that compatibility read path. The Windows profile and WSL session-preservation checks remain passing.
