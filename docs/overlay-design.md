# Optional fourth title-bar button: proposed design

Status: feasibility reviewed after 0.2 hardening and UI work; design only. No overlay code, injection or foreign subclassing is enabled. A prototype is not justified until real mixed-DPI and terminal-custom-chrome coverage is available. Manual hide/restore remains independent.

## Module boundary

Add a future `Nivetray.Overlay` Windows-only assembly depending on Core and Windows. An `OverlayCoordinator` selects an eligible foreground window from applications explicitly remembered by the user. `ICaptionGeometryProvider` returns a validated physical-pixel button rectangle or an incompatibility reason. An `OverlayWindow` is a separate borderless WPF window, hidden from the taskbar and Alt+Tab, with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`. Its command calls the same `WindowManager.HideAsync` used by manual controls, with a fresh identity check. The core must never depend on the overlay.

## Eligibility and placement

- Default off; require explicit opt-in per executable. Present the result of capability detection in settings.
- Initially show a single button only for the foreground eligible window. Avoid one WPF overlay per open application.
- Require standard caption controls and valid non-client geometry. Obtain caption button rectangles with bounded Win32 queries; use DWM extended frame bounds to account for invisible borders. Never place an overlay by merely subtracting a fixed number of pixels from the right edge.
- Place it adjacent to the existing minimize button only when there is a verified empty caption area. Never cover native buttons, application tabs, menus or resize borders. If geometry is ambiguous, hide the overlay and keep manual controls available.
- Hide for fullscreen, cloaked, minimized, invisible or destroyed targets, elevated/protected windows, hung windows, and unsupported custom chrome. Terminal title bars can contain tabs: classify them as unsupported until a tested geometry provider exists.

## Events, monitors and DPI

- Use `SetWinEventHook` with `WINEVENT_OUTOFCONTEXT` and skip own process. Subscribe narrowly to foreground, location, show/hide, minimize and destroy events. Never inject a hook DLL.
- Keep the delegate alive; unhook on disposal. Marshal events to the WPF dispatcher and coalesce location changes into at most one update per display frame. Revalidate identities after dispatch. On destroy, remove overlays immediately and discard obsolete event work.
- Track native coordinates in physical pixels, including negative monitor origins. Recalculate using `GetDpiForWindow`, monitor work areas, and `WM_DPICHANGED` when moving between displays. Convert to WPF device-independent units only at the rendering boundary; avoid multiplying scale factors twice.
- Reposition without activation or focus theft. Match target Z-order without making an unrelated persistent topmost overlay. Hide whenever foreground changes. Reevaluate maximize/restore, resize, display topology and resolution changes. No unconditional desktop-wide polling.

## Interaction and failure handling

The hit region belongs only to the new button, with an accessible name ("Hide window in Nivetray"), keyboard alternative via the main panel, tooltip and high-contrast support. A click uses the exact selected lifetime identity; failed validation hides the overlay and logs a categorical reason. Tray recovery remains available if the overlay fails. A per-app disable switch takes effect immediately. Exceptions dispose the overlay, never propagate into the foreign app or disable manual restoration.

The coordinator must honor the new recovery state machine: lost authorization can be reacquired only from a verified independent lifetime stamp; unavailable identities retain recovery and cannot authorize a click. Never restamp an ambiguous target during geometry discovery. An explicit overlay-eligibility setting must be separate from Favorite and Include helper windows. Do not interpret migrated Manage rules as opt-in. Queue click commands through the same serialized manager and disable its hit region while a target operation is pending.

## Implementation gate and acceptance matrix

Before enabling an overlay build:

1. Preserve all MVP identity, native server-liveness and restore-on-exit tests.
2. Test Windows 10 and 11, x64 and ARM64, at 100/125/150/200% scaling, mixed DPI, negative monitor origins, monitor disconnects and remote sessions.
3. Verify normal/maximized/snapped windows, resize drags, focus switches, minimize/restore, multiple windows in one process, handle reuse and target crashes.
4. Exercise standard Win32/WPF apps, CMD, Windows PowerShell, PowerShell 7, Windows Terminal, File Explorer and explicitly incompatible custom/fullscreen apps. Verify no native caption button is obscured.
5. Test keyboard, Narrator, high contrast, theme changes and no focus theft.
6. Measure CPU/RAM while idle and dragging between monitors. Require no repeated allocation growth or orphan overlay windows after a prolonged lifecycle test.

Until this matrix has recorded evidence, distribute the manual MVP and this proposal rather than silently enabling a potentially intrusive title-bar control.
