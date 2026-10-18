# Testing and release gates

```powershell
dotnet build Nivetray.sln -c Release
dotnet run --project tests/Nivetray.Tests -c Release --no-build
dotnet run --project tests/Nivetray.Tests -c Release --no-build -- --native
powershell -NoProfile -ExecutionPolicy Bypass -File tests/UiSmoke.ps1 -AppPath ./src/Nivetray.App/bin/Release/net10.0-windows/Nivetray.exe -OutputDirectory ./artifacts/qa/screenshots
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/BuildPortable.ps1 -Runtime win-x64
```

The console test executable is the runner; `dotnet test` does not execute this suite. CI runs deterministic tests without desktop requirements. `--native` and `UiSmoke.ps1` require a real interactive Windows desktop. Close existing Nivetray instances before the UI smoke test. It launches the app with a fresh `--data-directory` under ignored artifacts; it never rewrites the user's preferences. Its `AttachProcessId` option is only for explicitly owned test instances, not user applications.

Native tests use two isolated WinForms windows and a loopback TCP server. They cover sibling independence, normal/minimized/maximized states, fully off-screen restoration, real property loss, uncertain identity, rediscovery and process/window destruction. Property-removal helpers only receive fixture HWNDs. A dedicated-console / Windows PowerShell-child fixture attempts to allocate a separate host; if this desktop delegates it to an invisible pseudoconsole, it reports **SKIP**, not a pass. It never attaches to existing personal terminal windows.

The GUI smoke test checks instant search, manual selection, hide/restore/exit, local rules, themes, single-instance reopening, resize/maximize and tray events. It records warmed idle CPU/private/working set and repeated-refresh native/GDI counts. Small samples do not establish days-long leak freedom. Compare warmed batches instead of classifying native font initialization as a leak. Screenshots use per-monitor-aware `PrintWindow` on the owned application only. Public images include filtered fixtures, never user data or the settings path.

Manual accessibility checks still required: keyboard-only complete flows, Narrator, real Windows high contrast, additional monitor layouts, 125/200% scaling, sleep/resume and session shutdown. Do not change a user's system settings automatically to simulate these. Windows 10 and physical ARM64 execution require those actual environments.

Before a public release: all current-head checks green; inspect the full diff and packages; validate approved compatibility scope; review known recovery limits and screenshots; verify version/tag match; generate checksums; obtain explicit maintainer authorization. The workflow can create only a manually requested **draft** release; it never merges or publishes one automatically.

