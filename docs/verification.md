# Verification record — 0.2.0 hardening

Environment: Windows 11 Home x64, .NET SDK 10.0.401. No administrator rights used. Tests target only project-owned fixtures. Baseline commit: `6623a12` (PR #1); implementation continues on `codex/nivetray-mvp`.

## Executed validation

- Release solution builds with compiler/SDK analyzers (`AnalysisLevel=latest`): zero warnings and errors.
- Deterministic suite covers filtering, explicit exclusions, stale PID/HWND/start-time/generation, lost authorization, unavailable identities, native exceptions, queued exit behind pending hide, repeated cycles, malformed JSON backups, legacy rule migration, independent favorites/visibility, search, bounded tray policy and blocked resource IO/cancellation.
- Latest executed count: **67 deterministic + 16 native = 83 passed assertions**. The skipped dedicated-console probe does not contribute passing assertions.
- Interactive native suite verifies two independent WinForms windows, a TCP server responding while hidden, normal/maximized/minimized restoration, fully off-screen placement recovery, actual removal of authorization and lifetime properties, safe retry/retention, rediscovery and destruction. Native property-removal helpers receive only fixture HWNDs.
- Dedicated-console fixture: **SKIP** on this desktop. AllocConsole succeeds after detaching the child, but exposes no visible classic ConsoleWindowClass (consistent with terminal/pseudoconsole delegation). Windows PowerShell starts only inside this isolated fixture; its hidden-window behavior is not certified by this skipped case.
- UI Automation verifies filtered search, manual selection and row controls, hide/restore/restore-on-exit, favorite plus Standard visibility as independent JSON fields, rule search/removal, light/dark/system themes, tray right-click event / View all, second-instance reopening from a hidden panel, resize and maximize/restore.
- The owned WPF window reports per-monitor awareness **2** and was moved across both available monitors: **144 DPI and 96 DPI**, with UI screenshots captured at each position. This does not certify 125/200% scaling, monitor disconnect, every display topology or remote-session behavior.
- Public screenshots were captured from the actual app using only filtered fixtures. Available/light, hidden/dark and compact/monitor screenshots were visually inspected. Settings screenshots containing the local profile path are not published.

The baseline run exposed a fixture-close timeout; either fixture window now closes the pair. GUI testing exposed a live-menu rebuild race; discovery completion now leaves an open tray menu stable. A synthetic tray event is not a certification of every physical Explorer notification-area interaction. No personal processes were killed or suspended.

## Performance observations

All numbers below are short same-machine samples with UI Automation attached, not a benchmark or guaranteed memory budget. CPU is percent of one core over a two-second sample.

| Configuration / sample | Idle CPU | Private MiB | Working set MiB |
| --- | --- | --- | --- |
| Redesigned UI before software rendering | 0% | 243.6 | 316.7 |
| Same UI with software rendering, before DPI refinement | 0% | 102.5 | 160.3 |
| Per-monitor UI, representative subsequent run | 0% | 112.5 | 168.7 |

After native font/control warm-up, successive ten-refresh batches observed GDI counts 76 → 76 → 77 and, on another run, 74 → 74 → 74. Private memory was approximately 114–125 MiB after the batches. Some initial refreshes add native font objects; the test compares warmed successive batches instead of treating initialization as a leak. These measurements do not establish hours/days of leak freedom.

Frozen icons are bounded to 64 cached paths, with at most one resource loader outstanding and a 250 ms wait. Fault-injection tests hold IO indefinitely and verify nonblocking cache reads, bounded waits, no accumulation of workers, safe late results and shutdown cancellation. No cache lock spans shell IO. UNC icons are skipped. No idle polling or application-owned network client exists. Diagnostic messages deduplicate for one minute.

## Distribution and remaining gates

Portable packaging includes runtime, executable, license, public guides and docs/images. The script checks required ZIP entries and SHA-256; CI cross-publishes x64/ARM64. Public publishing and merging are never automatic. A manually requested version-tag workflow can create only a draft release.

Remaining manual/environment-specific coverage: Windows 10; execution on physical ARM64; CMD/conhost, Windows PowerShell, PowerShell 7, Windows Terminal/OpenConsole and app-specific hidden behavior; physical tray input across Explorer restarts; real high contrast and Narrator; 125/200% and additional mixed-DPI layouts; monitor disconnect/resolution changes (the off-screen fixture is a targeted regression, not a full hardware disconnect test); session switching, sleep/resume and prolonged soak tests.

No overlay is implemented. Its geometry, custom-caption compatibility and focus behavior require separate evidence before an optional prototype is enabled. See [testing](testing.md), [audit](audit.md), [safety](safety.md) and [overlay feasibility](overlay-design.md).
