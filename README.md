# Nivetray

**Keep it running. Keep it hidden.**

Nivetray is a small, open-source Windows tray utility that hides application **windows** without closing, suspending, or terminating their processes. Keep a development server, terminal session, or desktop application running while clearing your taskbar.

## MVP features

- Browse actual visible, titled top-level windows, with application name, executable icon and path.
- Search by title, application name or executable path. Windows from the same process remain separate entries.
- Hide a selected window and restore it individually or restore all.
- Use the tray menu to restore windows, open the searchable hidden-window panel, or access settings.
- Remember applications and configure explicit Include / Exclude rules by executable path. Remembered applications sort first; nothing is hidden automatically.
- Restore all tracked windows on normal exit. If restoration fails, exit is cancelled so you can retry.
- Save preferences locally as JSON. No telemetry, accounts, credentials, network service or administrator requirement.

The optional fourth title-bar button is **designed, not implemented** in this MVP. See [overlay design](docs/overlay-design.md) for the separate implementation and validation gate.

## Requirements and build

- Windows 10/11 are the target platforms; local execution has been verified on Windows 11 x64. Windows 10 and ARM64 execution still need compatibility runs. Microsoft's [.NET supported OS policy](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) applies to runtime vendor support, including the Windows 10 edition/lifecycle restrictions.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for building. Visual Studio with the .NET desktop workload is optional.

```powershell
git clone https://github.com/maxgab201/Nivetray.git
cd Nivetray
dotnet build Nivetray.sln -c Release
dotnet run --project src/Nivetray.App -c Release
```

The app runs with the current user's permissions (`asInvoker`). Elevated, protected, or inaccessible applications may be excluded. Do not elevate Nivetray for normal use.

## Use

1. Open the applications you want to manage. Launch Nivetray and click **Refresh**.
2. Search and select a window, then click **Hide selected window**.
3. Restore it from the tray's **Restore: …** entries or the **Hidden windows** tab. The tray menu also opens a searchable panel for long lists.
4. Select **Remember application** to prioritize that executable in future lists. Use **Settings** for rules.
5. Closing the panel keeps Nivetray in the tray by default. Choose **Restore all and exit** to stop it safely. Disable close-to-tray if you prefer closing the panel to restore everything and exit.

If no windows appear, open a conventional application and refresh. Invisible, untitled, cloaked, shell/system, and Nivetray's own windows are always excluded. Owned/helper and tool windows are excluded unless their executable has an **Include** rule. File Explorer windows are supported; the desktop and taskbar are excluded by window class. Console hosts are represented by the window-owning executable (for example `conhost.exe`, `OpenConsole.exe` or `WindowsTerminal.exe`), which can differ from the command running inside them. Nivetray manages whole windows, not individual terminal tabs.

## Portable package

```powershell
dotnet publish src/Nivetray.App -c Release -r win-x64 --self-contained true -o artifacts/portable/win-x64
Compress-Archive -Path artifacts/portable/win-x64/* -DestinationPath artifacts/Nivetray-win-x64.zip
```

Use `win-arm64` for native ARM64. Extract the **whole ZIP** into a writable location and run `Nivetray.exe`. No installer or preinstalled .NET runtime is needed for the self-contained package. Preferences remain in your user profile; portable refers to installation, not settings storage. Packages are unsigned and Windows may show a reputation warning.

GitHub Actions builds, tests and uploads both portable architectures on pushes and pull requests. Pushing a `v*` tag also publishes ZIPs and SHA-256 checksums as a GitHub release. ARM64 is cross-published; the CI test runner executes x64 tests.

## Tests

The dependency-free test executable returns a nonzero exit code on failure; **`dotnet test` is not the test runner** for this repository.

```powershell
# Deterministic filtering, identity, recovery and settings tests (also run in CI)
dotnet run --project tests/Nivetray.Tests -c Release

# Requires an interactive Windows desktop. Creates only its own test windows.
dotnet run --project tests/Nivetray.Tests -c Release -- --native

# WPF end-to-end smoke test; close existing Nivetray instances first.
dotnet build Nivetray.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/UiSmoke.ps1 `
  -AppPath ./src/Nivetray.App/bin/Release/net10.0-windows/Nivetray.exe
```

Native integration tests hide one of two windows from a dedicated fixture, verify the other is visible, confirm a TCP server still responds, and restore the hidden window. Fixtures exit automatically after 30 seconds. The UI smoke test exercises search, manual selection, hiding, restoration and restore-on-exit. Neither test targets existing user application windows.

## Architecture

```text
src/Nivetray.Core       Models, filtering, hide/restore lifecycle, JSON preferences
src/Nivetray.Windows    Win32 P/Invoke, enumeration, lifetime stamps, visibility requests
src/Nivetray.App        WPF panel, WinForms NotifyIcon, application lifecycle
tests/Nivetray.Tests    Deterministic tests and real-window/TCP fixture
tests/UiSmoke.ps1       WPF UI Automation integration test
docs/                  Safety model, overlay proposal, verification notes
```

Only the Windows adapter calls Win32 APIs. The core uses an injectable `IWindowPlatform`. There are no production NuGet dependencies. Detection runs on demand and when the panel is activated, with no background polling or process-list scanning. Icon caching is bounded. No injection, global hooks, process termination, or process suspension APIs are used.

## Safety, privacy and limitations

- Window identity includes HWND, process ID, process start time, thread, class and a per-window lifetime stamp. A second property authorizes hide/restore. Stale handles and removed stamps are rejected, including reuse within the same process. All identity checks are repeated before each visibility request. Win32 cannot make validation and a foreign-window operation atomic; see [safety notes](docs/safety.md).
- Visibility uses `ShowWindowAsync` and confirmation with a timeout so an unresponsive app does not block the panel. Failed operations remain available for recovery. Original normal, maximized or minimized state is used when restoring; restore does not force keyboard focus.
- Hiding does not terminate or suspend processes or their children. Some applications themselves throttle or alter behavior when hidden; Nivetray cannot override that behavior.
- Normal **Nivetray exit** restores tracked windows. Process crashes, forced termination, OS shutdown, or power loss cannot guarantee restoration. Do not force-stop Nivetray while it has hidden windows. There is no crash-recovery watchdog in this MVP. Tracked HWNDs are deliberately not persisted between runs.
- Apps with custom window lifecycles, exclusive fullscreen, protected/elevated processes, virtual desktops and console-host variations require compatibility testing. A window destroyed by its application is discarded, never replaced with another window from that process.
- `%LOCALAPPDATA%\Nivetray\settings.json` stores `CloseToTray` and executable rules. No window titles or live window handles are stored there. Malformed settings fall back to defaults and remain untouched until you explicitly save a preference.
- `%LOCALAPPDATA%\Nivetray\diagnostics.log` records error categories, with one rotated backup at 256 KiB. It omits titles, executable paths and credentials. Logs are local and never transmitted. Delete this folder after exiting Nivetray to remove its local data.

## Contributing

Keep changes modular, add a regression case for identity or restoration changes, run the documented tests, and include Windows version and reproduction steps in compatibility reports. Do not include sensitive window titles or paths in public reports. See [verification notes](docs/verification.md) for the current validation scope.

## License

[MIT](LICENSE) — Copyright (c) 2026 maxgab201 and Nivetray contributors.
