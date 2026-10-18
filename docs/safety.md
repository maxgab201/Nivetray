# Window safety model

Nivetray manages a window instance, never a process as a proxy for its windows.

## Identity and ownership

1. `EnumWindows` discovers visible, titled top-level windows. The adapter excludes Nivetray, known shell surfaces, cloaked windows and inaccessible executable identities. The core applies owned/tool-window and executable rules.
2. Discovery records HWND, PID, process start timestamp, owning thread and class. A unique per-run property name stores a monotonically increasing lifetime stamp on each discovered candidate. `SetProp` failing is an incompatibility, not a reason to request elevation. Native window destruction removes that property; a recycled HWND therefore fails comparison even if PID/thread/class are unchanged.
3. Before hiding, the manager checks identity and visibility again and assigns a separate authorization token. It records a recovery entry **before** dispatching hide.
4. Each `ShowWindowAsync` request and visibility confirmation checks both identity and authorization. Lost authorization does not invalidate identity: retain the entry and reacquire authorization only after verifying the independent lifetime stamp. A missing lifetime stamp or inaccessible process yields Unavailable, never assumed destruction. Rediscovery will not restamp an ambiguous matching tuple. A changed title does not invalidate a window.
5. Successful restore removes authorization and tracking only after repeated visibility samples and a bounded responsiveness check. Only confirmed destruction/replacement can discard an obsolete entry without restoring it. Failed/uncertain operations retain an actionable recovery message. A semaphore queues restore-all/exit behind an unfinished hide, and prune never mutates an in-flight operation. Exiting attempts every entry and is cancelled if any still need recovery. Discovery stamps are cleaned up on successful exit.

A restored non-minimized window entirely outside every current monitor is repositioned asynchronously onto the nearest work area, with no intentional resize, activation or Z-order change. Identity and authorization are checked again while confirming its return. This is a targeted fully off-screen recovery; partial visibility, custom chrome and minimized normal-position geometry still need compatibility testing.

Actual lifetime-stamp removal can leave an entry permanently unverifiable. Nivetray cannot safely invent a replacement stamp. It retains that record, explains the limit and remains usable in the tray; the app's own Show command may make its window visible, but does not itself prove lifetime identity. Exit stays guarded until recovery or confirmed destruction/replacement. This conservative limit is preferable to touching a potentially reused HWND.

Lifetime and authorization stamps protect against accidental reuse, not a malicious process. Stamps increase without reuse within a run; checked overflow fails before issuing a visibility request. Another process at the same integrity level can alter window properties. UIPI blocks many interactions with elevated processes. There is an unavoidable time-of-check/time-of-use race between identity validation and Win32 calls; user32 offers no atomic "validate lifetime and hide" operation. The implementation minimizes this interval and never claims an absolute lifetime guarantee. Asynchronous requests may complete after the confirmation timeout; keep Nivetray running and retry restore.

## Process execution

No `Kill`, `TerminateProcess`, `SuspendThread`, remote thread, injected DLL or process priority changes exist in production code. A terminal's window belongs to its host, not necessarily the shell/server process. Hiding an entire host window affects all tabs in that window. Some apps impose their own hidden-window behavior.

## Lifecycle

Only one Nivetray instance runs per user in a session. Closing the panel either hides it or follows the restore-before-exit path. The explicit exit controls in the panel and tray always use restore-before-exit. Windows session shutdown and crashes are outside that normal path. A future separately supervised recovery service would need a durable journal and stronger authenticated lifetime validation; never restore a persisted HWND blindly.

## Performance

The tray has no timer. Discovery runs on a worker at startup, Refresh and panel activation; overlapping requests coalesce, including a final refresh after settings change during discovery. Executable path lookup uses `PROCESS_QUERY_LIMITED_INFORMATION` and `QueryFullProcessImageName`, not module-memory access. Frozen icons are cached for at most 64 paths and loaded off the dispatcher. A show/hide request uses up to 800 ms of visibility sampling plus a bounded 200 ms WM_NULL responsiveness check. That check is not proof that every asynchronous message has drained, nor an atomic lifetime lock. Restore-all runs sequentially, so many incompatible windows can take longer. Errors retain recovery instead of guessing. Logs deduplicate and rotate without window content.

## API references

Icon IO never owns the cache lock. A single outstanding loader and a 250 ms caller wait prevent a stalled shell provider from accumulating workers or blocking UI cache reads. Safe exit cancels discovery waits after managed windows have been restored. A late icon result cannot authorize a window operation.

- [EnumWindows](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumwindows)
- [GetWindowThreadProcessId](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)
- [Window properties and lifetime](https://learn.microsoft.com/windows/win32/winmsg/window-properties)
- [SetProp and UIPI](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setpropw)
- [ShowWindowAsync](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-showwindowasync)
