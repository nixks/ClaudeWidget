# ClaudeWidget — Windows Tray Widget for Claude Usage

**Date:** 2026-07-16
**Status:** Approved design

## Purpose

A lightweight Windows tray widget that shows the user's Claude rate-limit usage at a glance:

- **Session (5-hour window) utilization %** — visible directly on the tray icon
- **Time until session reset**
- **Weekly (7-day window) utilization %** and its reset time

Inspired by [clawdometer-eink](https://github.com/nsyll/clawdometer-eink), which does the same for an e-ink device; this project ports the data-acquisition idea to a native Windows tray app.

## Decisions (settled with user)

| Decision | Choice |
|---|---|
| Form factor | System tray icon with live % + click-to-open flyout |
| Stack | C# / .NET 8, WinForms only (NotifyIcon + borderless flyout Form) |
| Data source | OAuth usage endpoint first; probe-request fallback |
| Extras (v1) | Color-coded icon, threshold toasts, start-with-Windows toggle, configurable poll interval |

## Architecture

Single-process .NET 8 WinForms tray application. No installer, no service, no admin rights. Distributed as one self-contained `.exe` (`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`).

A `System.Windows.Forms.Timer` drives polling on the UI thread; the HTTP call itself runs async so the UI never blocks.

### Project layout

```
ClaudeWidget/
  ClaudeWidget.sln
  src/ClaudeWidget/
    Program.cs              // mutex single-instance guard, run TrayAppContext
    TrayAppContext.cs       // ApplicationContext: owns NotifyIcon, timer, menu, wiring
    Core/
      CredentialsReader.cs  // read OAuth token from Claude Code credentials file
      UsageClient.cs        // usage endpoint + probe fallback, returns UsageSnapshot
      UsageSnapshot.cs      // immutable data model
      ThresholdNotifier.cs  // edge-triggered 80%/95% notification logic (pure, testable)
      Settings.cs           // load/save %APPDATA%\ClaudeWidget\settings.json
      Autostart.cs          // HKCU Run key get/set
    UI/
      IconRenderer.cs       // GDI+ draws the tray icon (number + color)
      FlyoutForm.cs         // borderless popup with bars + countdowns
  tests/ClaudeWidget.Tests/ // xUnit
```

## Components

### CredentialsReader

- Reads `%USERPROFILE%\.claude\.credentials.json`, JSON path `claudeAiOauth.accessToken`.
- Claude Code owns and refreshes this file; the widget only reads it.
- Re-reads the file whenever the API returns 401 (token may have been refreshed since last read). If still 401 after re-read, enter **auth-error state**.
- Missing file → **auth-error state** with tooltip "Claude Code credentials not found — is Claude Code installed and logged in?"

### UsageClient

**Primary — OAuth usage endpoint (zero token cost):**

```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <accessToken>
anthropic-beta: oauth-2025-04-20
```

Expected response (community-documented; endpoint is undocumented, so the parser must be defensive — treat missing fields as "unavailable", never throw):

```json
{
  "five_hour": { "utilization": 42, "resets_at": "2026-07-16T18:00:00Z" },
  "seven_day": { "utilization": 31, "resets_at": "2026-07-21T08:00:00Z" }
}
```

The parser must handle `utilization` arriving as either 0–1 fraction or 0–100 percent (normalize: values ≤ 1.0 are treated as fractions and multiplied by 100, matching how the rate-limit headers behave).

**Fallback — probe request (same method as clawdometer-eink):**

```
POST https://api.anthropic.com/v1/messages
Authorization: Bearer <accessToken>
anthropic-beta: oauth-2025-04-20
anthropic-version: 2023-06-01
body: { "model": "claude-haiku-4-5-20251001", "max_tokens": 1,
        "messages": [{ "role": "user", "content": "." }] }
```

Parse response headers (values are 0–1 fractions; resets are unix timestamps):

- `anthropic-ratelimit-unified-5h-utilization`, `anthropic-ratelimit-unified-5h-reset`
- `anthropic-ratelimit-unified-7d-utilization`, `anthropic-ratelimit-unified-7d-reset`

**Fallback policy:** each poll tries the usage endpoint; on failure (non-2xx or unparseable) it tries the probe in the same poll. Only if both fail does the poll count as a failure. The flyout's "last updated" line shows which source supplied the data.

### UsageSnapshot (model)

`SessionPercent`, `SessionResetsAtUtc`, `WeeklyPercent`, `WeeklyResetsAtUtc`, `FetchedAtUtc`, `Source` (UsageEndpoint | Probe). Countdown strings are computed at render time from `ResetsAtUtc`, so the tooltip/flyout stay correct between polls.

### TrayIcon (IconRenderer + NotifyIcon)

- Icon drawn at runtime with GDI+ at the system tray icon size: rounded-rect background + the session % number ("42"; "100" renders as "!!").
- Background color: **green** < 70, **amber** 70–89, **red** ≥ 90. **Gray** when stale or in auth-error (auth-error shows "!" instead of a number).
- Tooltip: `Claude — session 42%, resets in 2h 13m · week 31%` (append `(stale)` when stale). NotifyIcon tooltip is capped at 127 chars; keep it short.
- Left-click toggles the flyout. Right-click opens the context menu.

### FlyoutForm

Borderless, topmost, non-resizable Form positioned just above the tray area (from `Screen.PrimaryScreen.WorkingArea`), closed on deactivate (focus loss). Contents:

```
Claude Usage
Session   [██████████░░░░]  42%     resets in 2h 13m
Weekly    [████░░░░░░░░░░]  31%     resets Tue 21 Jul, 08:00
Updated 14:31:52 · usage endpoint
```

Bars use the same green/amber/red thresholds as the icon. Respects system dark/light mode via a simple registry check (`AppsUseLightTheme`).

### ThresholdNotifier

Pure class: given previous and current session %, plus the session reset timestamp, decides whether to fire the 80% ("Claude session usage at 80%") or 95% ("Claude session usage at 95% — nearly rate-limited") notification.

- Edge-triggered: fires only on upward crossing of a threshold.
- Re-arms when the session window resets (detected by `SessionResetsAtUtc` changing or % dropping sharply).
- Delivered via `NotifyIcon.ShowBalloonTip` (renders as a native toast on Win 10/11, zero packaging requirements).

### Settings + tray menu

`%APPDATA%\ClaudeWidget\settings.json`:

```json
{ "pollIntervalSeconds": 60, "warnThreshold": 80, "criticalThreshold": 95, "startWithWindows": false }
```

Right-click menu: **Refresh now** · **Start with Windows** (checkbox → `Autostart` writes/removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ClaudeWidget`) · **Poll interval** submenu (30 s / 60 s / 2 min / 5 min) · **Exit**. Changes made via the menu apply immediately and are persisted to the file on every change; the file is read only at startup (no file watcher — hand-editing it requires a restart).

## Error handling

| Condition | Behavior |
|---|---|
| Credentials file missing/unreadable | Gray "!" icon; tooltip explains; flyout shows guidance; keep retrying each poll |
| 401 from API | Re-read credentials once, retry; if still 401 → auth-error state |
| Network/API failure | Keep last snapshot; after **3 consecutive** failed polls, icon turns gray and tooltip/flyout marked "(stale)" |
| Both data sources fail long-term | Same stale handling; no crash, no error dialogs — the widget never interrupts the user except via the opted-in threshold toasts |
| Second instance launched | Named mutex; new instance exits silently |

## Testing

- **Unit (xUnit):** usage-endpoint JSON parser (both utilization scales, missing fields), probe header parser, `ThresholdNotifier` edge/re-arm logic, settings round-trip.
- HTTP layer behind an injected `HttpMessageHandler` so parsers are tested against canned responses.
- **Manual:** tray icon rendering at 100%/125%/150% DPI, flyout positioning, autostart toggle, live end-to-end fetch with the real credentials file.

## Out of scope (v1)

Per-model/token breakdowns, historical charts, reading `~/.claude/projects/**/*.jsonl` session logs, multi-account support, Claude Code hook integration (the e-ink repo's WORKING/DONE status), packaged installer / MSIX / Widgets-board integration.
