# WinTilix

A Windows tiling terminal with **synchronized input**, inspired by [Tilix](https://gnunn1.github.io/tilix-web/). Split a session into panes, type once, and drive every included pane at the same time — including an SSH cluster laid out like CSSH.

WinTilix is a native Windows app (WPF + ConPTY + WebView2). It is **not** a port of Tilix’s GTK/VTE codebase.

## Features

- **Tiled panes** — split right or down; drag the splitter to resize. Closing a pane keeps the rest of the tree intact.
- **Synchronized input** — session-wide toggle plus a per-pane keyboard control so you can include or exclude a pane.
- **SSH cluster** — paste one `user@host` per line; WinTilix opens a pane for each host (via Windows OpenSSH) and turns sync on.
- **Default shell** — prefers PowerShell 7 (`pwsh`), then Windows PowerShell, then `cmd`.
- **Per-monitor DPI** — `PerMonitorV2` awareness so tiles stay sharp on mixed-DPI setups.

## Requirements

- Windows 10 version 1809 or later (ConPTY / Windows Pseudo Console)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (already present on most Windows 11 machines)

SSH cluster mode needs `ssh.exe` (Windows OpenSSH, typically `C:\Windows\System32\OpenSSH\ssh.exe`).

## Build and run

```bash
dotnet build WinTilix.sln -c Release
dotnet run --project src/WinTilix/WinTilix.csproj -c Release
```

The WPF app copies `WinTilix.PtyHost.exe` next to `WinTilix.exe` after a successful build. Run the app from that output folder if you launch the executable directly:

`src/WinTilix/bin/Release/net8.0-windows/WinTilix.exe`

## Install and updates

Published versions are available from [GitHub Releases](https://github.com/buffalo-analytics/WinTilix/releases).
Run the `WinTilix-<version>-win-x64-setup.exe` asset to install a self-contained x64 build. The
installer adds WinTilix to the Start menu and offers a desktop shortcut; either shortcut can be
pinned to the taskbar using the standard Windows context menu.

WinTilix checks the latest stable GitHub Release in the background at startup. Use **Check for
updates** in the toolbar to see the installed and latest versions and open the release download
page. The check is informational: installing a newer release remains an explicit user action.

Releases are not code signed, so Windows SmartScreen shows a warning the first time you run the
installer. Choose **More info → Run anyway** to continue.

### Uninstall

WinTilix installs per user. Remove it from **Settings → Apps → Installed apps → WinTilix**, or run
`unins000.exe` from the install folder (`%LocalAppData%\Programs\WinTilix`).

### Tests

```bash
dotnet test WinTilix.sln -c Release
```

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+Shift+E` | Split focused pane to the right |
| `Ctrl+Shift+O` | Split focused pane down |
| `Ctrl+Shift+W` | Close focused pane |
| `Ctrl+Shift+I` | Toggle synchronized input for the session |
| `Ctrl+Shift+T` | New shell (split right) |

Toolbar buttons do the same actions. Each pane’s title bar has split, close, and **include in sync** (keyboard icon) controls.

## How synchronized input works

1. Turn **Synchronize input** on (toolbar or `Ctrl+Shift+I`).
2. Panes with the keyboard icon enabled receive a copy of what you type in the focused pane.
3. Turn the icon off on a pane to keep it visible but out of the broadcast.

This matches Tilix’s session-level sync: one session flag, plus a per-terminal participate flag.

## Project layout

| Path | Role |
| --- | --- |
| `src/WinTilix` | WPF UI, tiling host, WebView2 terminal panes |
| `src/WinTilix.Core` | ConPTY session client, shell resolution, input-sync hub |
| `tools/WinTilix.PtyHost` | Helper process that owns the Windows Pseudo Console |
| `tools/WinTilix.PtyCheck` | Small ConPTY diagnostics helper used by tests |
| `tests/WinTilix.Tests` | xUnit tests for session sync and ConPTY |

The extra projects under `tools/` (probes and session hosts) are development utilities and are not required to run the app.

## Status

This is an early public snapshot. Expect rough edges around terminal emulation, theming, and profiles.
CI builds and tests every change. Pushing a semantic version tag such as `v0.1.0` publishes the
self-contained installer as a GitHub Release.

## License

Licensed under the [MIT License](LICENSE).

Tilix is a separate project under the GNU GPL. WinTilix only takes inspiration from its tiling and synchronized-input workflow; it does not include Tilix source.
