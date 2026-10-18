# Security and recovery

Nivetray is a window utility, not a security isolation boundary. Other same-user processes can alter window properties. HWND validation cannot be made atomic with foreign Win32 operations. See the [safety model](docs/safety.md) for the exact guarantees and limitations.

If you find a recovery bug, keep Nivetray running and retry Restore. Do not force-stop it while windows are hidden. Report a sanitized reproduction through a GitHub issue; never attach private window titles, executable paths, credentials or unreviewed screenshots. The local diagnostics log contains categorical errors and IDs are copied only on explicit request.

For a sensitive vulnerability, use the repository's private vulnerability reporting feature if enabled. If it is unavailable, open an issue requesting a private contact channel without exploit details. This project does not claim a security certification, signed binary, or guaranteed crash/shutdown recovery. No public release should be treated as approved solely because a build succeeded.
