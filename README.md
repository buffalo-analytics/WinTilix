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

### Code signing (Buffalo Analytics / DigiCert)

Windows SmartScreen shows **Unknown publisher** for unsigned installers. That is expected until
Buffalo Analytics has a DigiCert code-signing identity wired into CI.

1. Buy a DigiCert **Code Signing** certificate for the legal entity **Buffalo Analytics**, hosted in
   DigiCert **KeyLocker / Software Trust Manager** (cloud HSM — required for GitHub Actions; a USB
   token alone will not work in CI).
2. Complete DigiCert organization validation (company docs / identity verification).
3. In DigiCert ONE, create a service user, API key, client authentication certificate (`.p12`), and
   a keypair / certificate profile whose subject will appear as the Windows publisher.
4. In the GitHub repo **Settings → Secrets and variables → Actions**, add:

   | Kind | Name | Value |
   | --- | --- | --- |
   | Variable | `SM_HOST` | DigiCert Software Trust host URL from your account |
   | Variable | `SM_KEYPAIR_ALIAS` | Keypair alias used to sign |
   | Secret | `SM_API_KEY` | Service-user API key |
   | Secret | `SM_CLIENT_CERT_PASSWORD` | Password for the `.p12` client cert |
   | Secret | `SM_CLIENT_CERT_FILE_B64` | Base64 of the `.p12` file |

   Encode the client cert on a trusted machine:

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes('.\client-auth.p12')) | Set-Clipboard
   ```

5. Push a new `vX.Y.Z` tag. The release workflow signs `WinTilix.exe`, `WinTilix.PtyHost.exe`, and
   the setup EXE when those values are present; otherwise it still publishes an unsigned installer.

Until reputation builds, SmartScreen may still warn occasionally even after signing; the publisher
name should change from **Unknown publisher** to **Buffalo Analytics**.

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
