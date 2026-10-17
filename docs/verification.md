# Verification record — 2026-10-17

## Completed locally

- Windows x64, .NET SDK 10.0.401.
- Release solution build: zero warnings, zero errors, warnings treated as errors.
- Dependency-free tests: 29 deterministic assertions covering filtering, explicit exceptions, sibling windows, duplicate hides, PID reuse, process-start reuse, same-process HWND generation reuse, stale selections and recovery entries, denied access, unconfirmed asynchronous requests, retry recovery, restore-all and JSON handling.
- Interactive native tests: 7 additional assertions with two windows owned by a dedicated fixture. Hiding one leaves the other visible; a loopback TCP server responds while hidden; process stays alive; restore-all confirms visibility; destroying the fixture invalidates identity. Total: **36 assertions passed**.
- WPF UI Automation smoke: search, select fixture, hide, restore individually, hide again, then exit through Settings. Both fixture windows are confirmed visible after Nivetray exits. The test also opens the panel through the tray double-click event path.
- Self-contained `win-x64` portable publish and ZIP generation succeeded; the published executable also passed the same WPF end-to-end smoke test. ZIP includes the full runtime and MIT license; no runtime installation is required.

Short warmed-up idle samples recorded 0% of one CPU core over two seconds, approximately 179–201 MiB private memory and 232–260 MiB working set with the panel open. These are development-machine observations, not a benchmark or guaranteed memory budget. WPF/runtime overhead and executable-icon loading remain optimization opportunities. The app has no idle polling loop.

## Automated pipeline

The workflow builds with .NET 10, runs the deterministic suite, cross-publishes self-contained x64 and ARM64 packages, and attaches ZIPs plus SHA-256 checksums as workflow artifacts. Tag-triggered releases are configured but publishing a release is not part of the initial implementation. UI/native tests require an interactive desktop and remain explicit local commands.

## Remaining compatibility coverage

Windows 10, ARM64 execution, mixed-DPI/multi-monitor setups, each terminal host (CMD, Windows PowerShell, PowerShell 7, Windows Terminal), elevated apps, virtual desktops, app-specific hidden-state behavior and prolonged soak testing need separate runs. The fixture proves native mechanics and continuing server execution; it does not certify every application. No overlay is implemented or represented as tested. See [overlay design](overlay-design.md) for its acceptance matrix.
