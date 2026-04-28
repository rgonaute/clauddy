# Clauddy for Windows — Design Spec

**Date:** 2026-04-28
**Status:** Approved (pending user sign-off on this doc)
**Owner:** ron@cashcow.com

## Summary

A persistent always-on-top desktop widget for Windows 10/11 that mirrors the
state of every active Claude Code session — native, WSL, or both. Modeled on
the upstream [`bugzmanov/divoom-minitoo`](https://github.com/bugzmanov/divoom-minitoo)
Clauddy app, but with the physical Divoom MiniToo replaced by a virtual
on-screen widget, and with multi-session support added so several parallel
Claude Code sessions can be glanced at simultaneously.

## Goals

1. **Glance, don't switch.** While juggling 2–5 Claude Code sessions across
   multiple terminals, the user should be able to look at one corner of the
   screen and see which session is working, which is waiting on them, and
   which is idle — without alt-tabbing.
2. **No friction.** The widget must never block, slow down, or otherwise
   intrude on Claude Code itself, even if the widget is crashed or not
   running.
3. **Trivial to install.** Single signed `.exe` installer, per-user, no
   admin rights, no .NET runtime download, no Visual C++ redistributable
   prompt.

## Non-goals (v1)

- Remote / SSH session monitoring (deferred to v2 — needs tunneling).
- Tile interactivity beyond drag (no click-to-focus-terminal, no peek
  popovers; they're v1.5+ once the core widget proves useful).
- Sound or system notifications on `alerting` state. Visual only.
- Cross-platform (macOS/Linux). Windows-only by design — upstream covers
  macOS already.
- Custom artwork. v1 ships with the upstream Clauddy GIFs (with
  attribution); a fresh art pass is a later concern.

## User experience

### What the user installs

A single signed `Clauddy-Setup.exe`, ~30 MB, that:
1. Copies `Clauddy.exe` to `%LOCALAPPDATA%\Clauddy\`.
2. Creates a Start Menu shortcut and (opt-in) a "Run at login" registry
   entry.
3. On first launch, walks the user through hook installation:
   - Asks before editing `~\.claude\settings.json` on the Windows side.
   - Lists detected WSL distros with checkboxes — only edits the ones the
     user ticks.
   - Records every change in `~\.clauddy\installed-hooks.json` so uninstall
     can cleanly back them out.

### What the widget looks like

Reference: brainstorm session screen `gifs-native.html`. Final tile is the
chunky black-bordered frame from the mockup, ~104×124 px, containing the
upstream `working.gif` / `alerting.gif` / `chilling.gif` at full size with
no cropping. Tiles flow horizontally, one per active session, with a small
grip handle on the left edge for dragging.

The window is borderless, transparent (only tiles paint, not a window
box), always-on-top, has no taskbar entry, and never steals focus when
clicked.

### State model

Three states, mirroring upstream exactly:

| State | When the tile shows it |
|---|---|
| **chilling** | Session just started, or Claude finished its turn and is idle. |
| **working** | User submitted a prompt; Claude is processing. |
| **alerting** | Claude is blocked waiting on the user (permission prompt, etc.). |

### Session identity and labels

Each Claude Code session shows up as a separate tile (so two sessions in
the same repo don't collapse into one).

Tile label is computed by the widget from the session's working directory:
- If `CLAUDDY_LABEL` env var is set in the terminal where Claude was
  launched → use that string.
- Else if the cwd is inside a git repo → `<repo>@<branch>`
  (e.g. `clauddy@main`).
- Else → basename of the cwd.

### Tile lifecycle

A tile is removed:
1. **Immediately** when Claude Code exits cleanly (the `SessionEnd` hook
   fires).
2. As a **fallback**, after 30 minutes of no hook activity from that
   session — covers crashes and force-kills.

A tile never stays around as a "ghost" — when it's gone, it's gone.

### Configuration surface

The widget has no settings window. Everything is on the tray icon's
right-click menu:

- Show widget / Hide widget
- Reset position (snap back to default corner)
- Run at login (toggle, off by default)
- Manage hooks… (re-run the first-run hook installer)
- About
- Quit

Window position persists across restarts. If the saved position is on a
monitor that no longer exists, the widget snaps back to the primary
monitor's bottom-right corner.

## Technical architecture

### Process model

Two pieces. No daemon, no service, no admin.

```
Claude Code session → hook script → POST → Clauddy.exe (WPF, single EXE)
```

Hooks fire on five Claude Code events (`SessionStart`,
`UserPromptSubmit`, `Stop`/`SubagentStop`, `Notification`, `SessionEnd`).
Each hook runs a small bash or PowerShell script that POSTs a JSON
payload to the widget's localhost HTTP listener. If the widget isn't
running, the POST silently no-ops with a 1-second timeout — Claude Code
is never blocked.

### Tech stack

- **C# / .NET 8 / WPF** for the widget.
- Single-file self-contained EXE via `dotnet publish -c Release -r
  win-x64 --self-contained -p:PublishSingleFile=true`. ~30 MB, no .NET
  runtime install required.
- `Hardcodet.NotifyIcon.Wpf` for the tray icon.
- Built-in `HttpListener` on `127.0.0.1:<ephemeral port>`.

### Port discovery

At startup the widget binds an ephemeral port and writes the URL to
`%USERPROFILE%\.clauddy\endpoint` (e.g. `http://127.0.0.1:51797`). Hook
scripts read that file before each POST. WSL distros read it via
`/mnt/c/Users/<winuser>/.clauddy/endpoint`; `<winuser>` is captured as
`WIN_USERNAME` in `~/.profile` by the WSL portion of the hook installer
so the script doesn't have to guess.

### Widget components

| Component | Responsibility |
|---|---|
| HTTP listener | Routes `POST /state` and `GET /healthz`. Marshals updates onto the WPF dispatcher. |
| Session store | In-memory dictionary keyed by `session_id`. Fields: `label`, `state`, `cwd`, `lastSeen`, `source`. |
| Render layer | Borderless transparent topmost window. ItemsControl bound to the session collection. Tile = chunky frame + GIF. |
| Lifecycle manager | `DispatcherTimer` GCs stale sessions every 30s. `SessionEnd` removes immediately. |
| Tray icon | Menu items listed under "Configuration surface" above. |
| Settings store | `%APPDATA%\Clauddy\settings.json` — window position, run-at-login flag. |

### Hook script

A single `clauddy-hook.sh` (with a `.ps1` sibling) handles all five
events. Pseudocode:

```bash
endpoint=$(cat ~/.clauddy/endpoint 2>/dev/null) || \
  endpoint=$(cat /mnt/c/Users/$WIN_USERNAME/.clauddy/endpoint 2>/dev/null) || exit 0

payload=$(cat)
event=$(jq -r '.hook_event_name' <<<"$payload")
session_id=$(jq -r '.session_id' <<<"$payload")
cwd=$(jq -r '.cwd' <<<"$payload")

case "$event" in
  SessionStart) state=chilling ;;
  UserPromptSubmit) state=working ;;
  Stop|SubagentStop) state=chilling ;;
  Notification) state=alerting ;;
  SessionEnd) action=remove ;;
  *) exit 0 ;;
esac

curl --max-time 1 -sS -X POST "$endpoint/state" \
  -H 'Content-Type: application/json' \
  -d "{\"session_id\":\"$session_id\",\"cwd\":\"$cwd\",\"label\":\"${CLAUDDY_LABEL:-}\",\"state\":\"$state\",\"action\":\"${action:-update}\"}" \
  >/dev/null 2>&1 || true
```

The two non-negotiables: `--max-time 1` and `|| true`. Claude Code is
never delayed or failed by this script.

### Window flags (the always-on-top behavior)

- `Topmost = true`
- `AllowsTransparency = true` + `WindowStyle = None` + transparent
  background brush
- `WS_EX_TOOLWINDOW` extended style → no Alt-Tab, no taskbar entry
- `WS_EX_NOACTIVATE` → clicking the grip doesn't steal focus from the
  user's terminal

### Errors and recovery

- Hook script fails or widget down → silent no-op. Claude Code unaffected.
- Widget receives malformed JSON → 400, logged, no state change.
- Widget logs to `%APPDATA%\Clauddy\log.txt`, rotated at 1 MB, last 3
  files retained.
- Widget crash → user restarts via Start Menu shortcut. v1 has no
  watchdog/relauncher.

## Distribution

- Single signed `Clauddy-Setup.exe` (Inno Setup or WiX MSI wrapped in a
  bootstrapper). Per-user install at `%LOCALAPPDATA%\Clauddy\`. No admin.
- GitHub Releases for distribution; auto-update is **not** in v1.
- Uninstaller backs out hooks from every Windows + WSL `settings.json`
  it touched (per the `installed-hooks.json` ledger).

## Open questions for v1.5+

- Click-to-focus on a tile (B from interaction-model brainstorm).
- Click-to-peek showing last assistant message (C from same).
- Sound on `alerting`.
- Custom artwork (own pixel art instead of upstream GIFs).
- Auto-update.
- Remote / SSH-session monitoring.

## Attribution

This project is modeled directly on the Clauddy app from
[`bugzmanov/divoom-minitoo`](https://github.com/bugzmanov/divoom-minitoo).
The three GIF assets (`working.gif`, `alerting.gif`, `chilling.gif`) are
bundled from that repository with credit until a fresh art pass is done.
