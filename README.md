# Clauddy for Windows

A persistent always-on-top desktop widget that mirrors your Claude Code session state.
Multi-session aware. Inspired by [bugzmanov/divoom-minitoo](https://github.com/bugzmanov/divoom-minitoo).

## Install

Download `Clauddy-Setup.exe` from the [Releases](#) page and run it.
Per-user install, no admin rights required.

On first run, a wizard asks where to install hooks: Windows native shells
(Git Bash, PowerShell) and any WSL distros you have. Tick what you want,
click Install. Restart any open Claude Code sessions.

## How it works

Each Claude Code session reports its state via hook scripts that POST to
the widget over loopback HTTP. The widget shows one tile per active
session — `working` / `alerting` / `chilling`. See
[design spec](docs/superpowers/specs/2026-04-28-clauddy-windows-design.md).

## Tile labels

By default a tile shows `<repo>@<branch>` (e.g. `clauddy@main`). Override
in any terminal:

    export CLAUDDY_LABEL="migration-agent"

## Tray menu

- **Show / Hide widget**
- **Reset position** — snaps back to bottom-right
- **Run at login** — toggle autostart
- **Manage hooks…** — re-run the install wizard
- **Quit**

## Logs

`%APPDATA%\Clauddy\log.txt` (rotated at 1 MB, last 3 files kept).

## Uninstall

Settings → Apps → Clauddy → Uninstall. Hooks are cleanly removed from
`~/.claude/settings.json` (Windows + every WSL distro you installed
into).

## Build from source

```bash
dotnet test
dotnet publish src/Clauddy -c Release -o publish
iscc installer/Clauddy.iss
```

Output: `installer/Output/Clauddy-Setup.exe`.

## Attribution

GIF assets are bundled from
[bugzmanov/divoom-minitoo](https://github.com/bugzmanov/divoom-minitoo)
with thanks.
