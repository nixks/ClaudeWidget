# ClaudeWidget

Windows system-tray widget showing your Claude rate-limit usage at a glance:
session (5-hour window) % on the icon, with a click flyout showing session and
weekly usage bars and reset countdowns.

Inspired by [clawdometer-eink](https://github.com/nsyll/clawdometer-eink).

## Requirements

- Windows 10/11
- Claude Code installed and logged in (the widget reads the OAuth token from
  `%USERPROFILE%\.claude\.credentials.json`; it never writes to it)

## Build & run

```powershell
dotnet run --project src/ClaudeWidget          # run from source
dotnet test                                    # run unit tests
dotnet publish src/ClaudeWidget/ClaudeWidget.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The published `publish\ClaudeWidget.exe` is fully self-contained.

## Usage

- **Tray icon**: session usage %. Green < 70, amber 70–89, red ≥ 90, gray = stale/no data.
- **Left-click**: flyout with session + weekly bars and reset times.
- **Right-click**: Refresh now · Start with Windows · Poll interval · Exit.
- Toast notifications fire when session usage crosses 80% and 95%
  (thresholds editable in `%APPDATA%\ClaudeWidget\settings.json`, applied on restart).

## How it gets the data

Polls Anthropic's OAuth usage endpoint (no tokens consumed). If that fails, it
falls back to a minimal probe request and reads the
`anthropic-ratelimit-unified-*` response headers — the approach used by
clawdometer-eink.
