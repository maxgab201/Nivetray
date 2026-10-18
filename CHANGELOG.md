# Changelog

## 0.2.0 — Unreleased

- Preserve recovery entries after lost authorization or inconclusive identity reads; reacquire authorization only with a verified independent lifetime identity.
- Serialize hide/restore/exit, retain failed/pending operations, and require repeated visibility plus bounded responsiveness confirmation.
- Redesign the WPF workspace with per-row actions, useful empty states, recovery guidance, keyboard shortcuts, light/dark/system themes and Windows high-contrast resources.
- Limit the tray to eight recent windows and a searchable View all panel; open the existing instance on relaunch.
- Discover windows and load bounded executable icons off the dispatcher; use limited process-query rights, deduplicate logs and reduce memory with software rendering.
- Migrate existing rule meanings without deleting preferences; version JSON and back up malformed files before explicit saves.
- Expand deterministic, native and UI Automation tests; include docs and verified checksums in portable packages. Public releases remain manual.

## 0.1.0 — Initial MVP

- Filtered window discovery, manual hide/restore, notification-area menu, local executable rules, MIT license and x64/ARM64 portable build workflow.
