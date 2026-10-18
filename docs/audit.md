# Hardening audit

Baseline: PR #1, commit `6623a12`, Windows 11 x64, .NET 10.0.401. All production modules, project manifests, tests, workflow and documentation reviewed before changing implementation. Baseline build and interactive regression suite rerun locally.

| Severity | Finding | Required correction |
| --- | --- | --- |
| Critical | `RestoreAsync` and `Prune` remove recovery entries when authorization disappears, conflating destruction with a live hidden window. | Separate verified destruction/replacement from unknown identity and lost authorization; retain uncertain entries; reacquire authorization only after independent lifetime validation. |
| High | A boolean identity check conflates access errors, missing lifetime stamps and destroyed windows. | Explicit identity inspection result; never drop an entry on an inconclusive read. |
| High | Async hide timeout does not establish whether a delayed request can still execute. | Keep pending recovery state, serialize operations, and use a bounded message-queue fence before declaring restore complete. |
| Medium | Enumeration and icon extraction execute on the dispatcher. | Move discovery and icon IO off the UI thread, coalesce refreshes and bound caches. |
| Medium | Tray menu grows with every window and retains stale labels until a refresh. | Limit recent entries; add searchable View all; update on opening and after operations; dispose replaced menus. |
| Medium | Light-only UI, fixed action layout, unclear rule modes and weak keyboard discovery. | Theme resources, high contrast, per-row actions, clear statuses/empty states, accessible names, shortcuts and separate rules navigation. |
| Medium | Malformed preferences can be overwritten by the next settings change; no schema version or migration coverage. | Version preferences, validate, preserve backup before save, migrate v1 semantics and test. |
| Low | Package README references documents not included in ZIP; releases are automatically public on a tag. | Include public documentation; use manually requested draft releases and minimum permissions. |
| Low | No contribution/security/change guides or issue forms. | Add concise public English project documentation and support templates. |

No injection, process termination/suspension or production network APIs found. Retain the modular Core / Windows / WPF boundary and MIT license. Avoid a web framework or additional production packages. Overlay stays off; its feasibility is reviewed after safety and UI changes.

Validation records and remaining hardware-dependent checks belong in `verification.md`. Findings above describe the audited baseline, not a claim that every correction has already shipped.
