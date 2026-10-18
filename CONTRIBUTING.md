# Contributing to Nivetray

Use Windows with .NET 10 SDK. Build with `dotnet build Nivetray.sln -c Release`; follow [testing](docs/testing.md) before submitting a PR. Keep Core independent of WPF and Win32. Add regression tests for identity, recovery, rules or preference changes. Prefer small, descriptive commits and avoid new dependencies unless they solve a demonstrated problem.

Recovery comes first. Never remove an uncertain recovery entry, restore an unverified HWND, terminate/suspend another process, persist live HWNDs across sessions, add telemetry, or require elevation for normal use. Validate queued operations and partial failures. See [safety](docs/safety.md).

Keep public docs in English, preserve MIT licensing, and include real reproduction steps and the scope of validation. Do not claim Windows 10, ARM64 execution, multiple monitors or assistive-technology coverage based on compilation alone. Screenshots must show only isolated fixtures; exclude personal titles, paths and data. Issue reports should use sanitized examples.

For UI changes, check light/dark/system themes, Windows high contrast, keyboard focus, accessible names, empty/search/error states and minimum window size. Never run invasive tests against the contributor's personal apps. Use the provided fixtures.

Release approval remains a maintainer action. CI generates portable artifacts. A manually requested draft release is not authorization to publish it publicly or merge a PR.
