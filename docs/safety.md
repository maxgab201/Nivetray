# Window safety model

Nivetray manages a window instance, never a process as a proxy for its windows.

## Identity and ownership

1. `EnumWindows` discovers visible, titled top-level windows. The adapter excludes Nivetray, known shell surfaces, cloaked windows and inaccessible executable identities. The core applies owned/tool-window and executable rules.
2. Discovery records HWND, PID, process start timestamp, owning thread and class. A unique per-run property name stores a random lifetime stamp on each discovered candidate. `SetProp` failing is an incompatibility, not a reason to request elevation. Native window destruction removes that property; a recycled HWND therefore fails comparison even if PID/thread/class are unchanged.
3. Before hiding, the manager checks identity and visibility again and assigns a separate authorization token. It records a recovery entry **before** dispatching hide.
4. Each `ShowWindowAsync` request and visibility confirmation checks both identity and authorization. Only entries held in the current manager can be restored. Process replacement, destruction or property loss invalidates the entry. A changed title does not invalidate a window.
5. Successful restore removes authorization and tracking. A failed visibility request remains tracked so the user can retry. Exiting attempts every entry and is cancelled if any still need recovery. Discovery stamps are cleaned up on successful exit.

Random property values protect against accidental reuse, not a malicious process. Another process at the same integrity level can alter window properties. UIPI blocks many interactions with elevated processes. There is an unavoidable time-of-check/time-of-use race between identity validation and Win32 calls; user32 offers no atomic "validate lifetime and hide" operation. The implementation minimizes this interval and never claims an absolute lifetime guarantee. Asynchronous requests may complete after the confirmation timeout; keep Nivetray running and retry restore.

## Process execution

No `Kill`, `TerminateProcess`, `SuspendThread`, remote thread, injected DLL or process priority changes exist in production code. A terminal's window belongs to its host, not necessarily the shell/server process. Hiding an entire host window affects all tabs in that window. Some apps impose their own hidden-window behavior.

## Lifecycle

Only one Nivetray instance runs per user in a session. Closing the panel either hides it or follows the restore-before-exit path. The explicit exit controls in the panel and tray always use restore-before-exit. Windows session shutdown and crashes are outside that normal path. A future separately supervised recovery service would need a durable journal and stronger authenticated lifetime validation; never restore a persisted HWND blindly.

## Performance

The tray has no timer. Enumeration occurs at startup, Refresh and panel activation. Only visible titled windows are inspected; executable icons are cached with a maximum of 128 paths. An individual show/hide request waits at most 800 ms for confirmation without blocking the dispatcher. Restore-all runs sequentially, so many incompatible windows can take longer. Logs rotate locally and carry no window content.

## API references

- [EnumWindows](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumwindows)
- [GetWindowThreadProcessId](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)
- [Window properties and lifetime](https://learn.microsoft.com/windows/win32/winmsg/window-properties)
- [SetProp and UIPI](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setpropw)
- [ShowWindowAsync](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-showwindowasync)
