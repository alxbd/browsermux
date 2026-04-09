# Logger

BrowserMux uses a minimal file logger (`AppLogger`) that writes a single plain-text log file, watchable live with `tail -f` or `Get-Content -Wait`. It's intentionally tiny: no log framework, no async queue, no levels filtering, no multi-sink fan-out.

---

## Where it lives

| File | Role |
|---|---|
| `src/BrowserMux.Core/Services/AppLogger.cs` | The logger itself (static class, ~45 lines) |
| `src/BrowserMux.Core/AppInfo.cs` | `LogPath` constant resolved from `%LOCALAPPDATA%\BrowserMux\logs\app.log` |

---

## How it works

- **Static, lock-based.** `AppLogger` is a `static class` with a single `_lock` object. Every `Write` serializes through it, so concurrent callers from any thread are safe but serialized.
- **Three entry points.** `Info`, `Warn`, `Error`. `Error` optionally takes an `Exception` and appends `→ {ExceptionType}: {Message}` (no stack trace — unhandled crashes go to `%TEMP%\BrowserMux_crash.txt` instead).
- **Line format.** `HH:mm:ss.fff [LEVEL] message` — millisecond timestamp, no date (the file is short-lived enough that date would be noise).
- **Append-only.** Each `Write` calls `File.AppendAllText`. Failures are swallowed: the logger must never crash the app.
- **Debug mirror.** Every line is also sent to `System.Diagnostics.Debug.WriteLine` so it appears in the VS Output window during debugging.
- **Directory auto-create.** The static constructor creates `%LOCALAPPDATA%\BrowserMux\logs\` if missing, then runs `TrimLog()` once.

## Rotation

There is exactly **one** rotation trigger: the static constructor, which runs the first time `AppLogger` is touched (i.e. at app startup).

- If `app.log` already exists and has more than **500 lines**, it's rewritten to keep only the **last 400 lines**.
- Otherwise it's left untouched.
- `TrimLog()` reads the whole file into memory and rewrites it. Acceptable because it runs at most once per process lifetime and the file is small by construction.

There is **no in-session rotation, no size-based rotation, no archival `.1` / `.2` files, no auto-purge of the crash dump.**

## Trade-offs

This design is a deliberate minimum. The trade-offs are:

- **Pro — zero complexity.** No timers, no background threads, no rotation race conditions, no config. The logger has never been the cause of a bug.
- **Pro — tailing works perfectly.** A single append-only file is trivial to watch live.
- **Con — unbounded during a session.** BrowserMux lives in the tray and can run for days without restart. Between restarts, nothing caps `app.log`'s growth; a chatty session can theoretically push it into the MB range before the next trim at boot.
- **Con — no history across restarts.** Every startup truncates to the last 400 lines. If the app restarted recently, the logs from before the restart may already be gone (though crash context is preserved separately in `BrowserMux_crash.txt`).
- **Con — `TrimLog` reads the whole file.** Fine at 500 lines, would not scale if the cap grew.

### When this becomes a problem

The current design is accepted as long as `app.log` doesn't exceed a few MB in practice. If a future feature (e.g. verbose routing-rule tracing, IPC debugging) starts producing logs fast enough to make multi-MB sessions common, the upgrade path is:

1. Add a write counter in `AppLogger.Write`, trigger `TrimLog()` every N writes (e.g. 200) under the existing lock. Still no timer, still no thread.
2. Or switch the cap from line-count to file-size (`FileInfo(LogPath).Length > N`).

Both are ~10 lines of code and backwards-compatible with the tailing workflow. Not done yet because the current usage doesn't justify it.

---

## Crash dump (adjacent)

Unhandled exceptions bypass `AppLogger` entirely and land in `%TEMP%\BrowserMux_crash.txt`, written by the `UnhandledException` handler in `App.xaml.cs`. That file is **overwritten at each crash**, never purged, and never rotated. See the features inventory for the user-visible description ([features.md](features.md) → "Crash dump").

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| No `app.log` file | App never logged anything, or `%LOCALAPPDATA%\BrowserMux\logs\` is not writable | Check permissions; the static ctor swallows the directory-create error |
| Log appears truncated after restart | Expected — boot-time trim kept only the last 400 lines | Not a bug; capture logs before restart if you need the older context |
| `app.log` is several MB | Long-running session with verbose logging; in-session cap doesn't exist | Restart the app to trigger the trim, or implement the write-counter upgrade above |
| Log lines interleaved / garbled | Should not happen — all writes go through `_lock` | Check for a second process; the single-instance mutex should prevent this |
