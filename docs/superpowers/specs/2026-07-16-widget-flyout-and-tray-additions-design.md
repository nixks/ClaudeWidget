# ClaudeWidget — Flyout & Tray Icon Additions

**Date:** 2026-07-16
**Status:** Approved design
**Builds on:** `docs/superpowers/specs/2026-07-16-claude-usage-tray-widget-design.md` (v1, merged to `master`)

## Purpose

Five follow-up requests against the shipped v1 widget:

1. 10px of breathing room at the bottom of the flyout.
2. The flyout should be draggable, and remember where you put it.
3. A "sticky" pin so the flyout can be told to stay open instead of closing on click-away.
4. A manual refresh button directly on the flyout (today "Refresh now" only exists in the tray right-click menu).
5. Fix: Windows' built-in "Always show this icon" setting (Settings → Personalization → Taskbar → Select which icons appear on the taskbar) does not stick for ClaudeWidget — the icon always ends up back behind the `^` overflow chevron after a relaunch.

## Root cause of #5

WinForms' `NotifyIcon` does not give Windows a stable per-icon identity across app relaunches. Windows persists the "always show" preference against an icon identity that, for `NotifyIcon`-based apps, is not guaranteed stable — so on the next launch Windows can't match the new icon instance to the one you pinned, and it resets into the overflow area. The supported fix is to give Shell a fixed **GUID** for the icon (the `guidItem` field of `NOTIFYICONDATA`), which `NotifyIcon` does not expose. There is no configuration-only fix; it requires talking to the tray directly via `Shell_NotifyIcon` P/Invoke instead of `System.Windows.Forms.NotifyIcon`.

## Decisions (settled with user)

| # | Decision |
|---|---|
| 1 | Flyout `ClientSize` height 160 → 170, no other layout changes — adds a clean 10px gap below the last line of text. |
| 2 | Dragging repositions the flyout; the final position is written to `settings.json` and reused (clamped to the current virtual screen) on every future open, including after an app restart. |
| 3 | Pin toggle: off = today's click-away-closes behavior; on = stays open until unpinned or explicitly closed. Pin state is **in-memory only** — every launch starts unpinned. |
| 4 | Refresh button on the flyout triggers the same poll as the tray menu's "Refresh now"; it visually dims and ignores clicks while a fetch is in flight. |
| 5 | Replace `NotifyIcon` with a custom `Shell_NotifyIcon`-based tray icon carrying a fixed, hardcoded GUID, so Windows can persistently remember pin/overflow state across relaunches. |

## Architecture

All five items are additive changes to the existing three files (`Settings.cs`, `FlyoutForm.cs`, `TrayAppContext.cs`) plus one new file (`TrayIcon.cs`) that replaces `NotifyIcon`. No changes to `Core/UsageClient.cs`, `UsageParser.cs`, `CredentialsReader.cs`, `ThresholdNotifier.cs`, `Formatting.cs`, `Autostart.cs`, or `IconRenderer.cs` — those are untouched.

### File changes

```
src/ClaudeWidget/
  Core/
    Settings.cs        // MODIFY: add FlyoutX, FlyoutY (nullable int?)
  UI/
    FlyoutForm.cs       // MODIFY: padding, drag, pin, refresh button, glyph rendering
    TrayIcon.cs         // NEW: Shell_NotifyIcon wrapper with fixed GUID, replaces NotifyIcon
  TrayAppContext.cs      // MODIFY: use TrayIcon instead of NotifyIcon; wire drag/pin/refresh callbacks
```

## Feature 1 — Bottom padding

`FlyoutForm`'s constructor: `ClientSize = new Size(340, 160)` → `new Size(340, 170)`. Nothing else moves; the extra 10px lands entirely below the existing "Updated …" line (drawn at fixed `y = 138`).

## Feature 2 — Draggable, position remembered

**Settings.cs** gains two nullable properties:

```csharp
public int? FlyoutX { get; set; }
public int? FlyoutY { get; set; }
```

Both `null` until the user drags the flyout for the first time (so `Settings.Load`'s existing behavior — defaults when the file is missing/corrupt — is unaffected; a fresh `Settings()` naturally has `FlyoutX = FlyoutY = null`).

**FlyoutForm.cs**: drag is implemented with standard mouse-down/move/up tracking (no OS-native caption drag, since the form has no caption — `FormBorderStyle.None`):

- `OnMouseDown`: if the click point is **not** inside the pin or refresh glyph hit-boxes (see Feature 3/4), record `_dragStartScreenPoint = PointToScreen(e.Location)` and `_dragStartFormLocation = Location`; set `_dragging = true`.
- `OnMouseMove`: if `_dragging`, compute `Location = _dragStartFormLocation + (Size)(Cursor.Position - _dragStartScreenPoint)`.
- `OnMouseUp`: if `_dragging`, set `_dragging = false` and raise `public event Action<Point>? PositionChanged;` with the final `Location`.

`ShowNearTray()` changes signature to accept the saved position:

```csharp
public void ShowNearTray(Point? savedPosition)
{
    var area = Screen.PrimaryScreen!.WorkingArea;
    var defaultLocation = new Point(area.Right - Width - 8, area.Bottom - Height - 8);
    Location = savedPosition is { } p && SystemInformation.VirtualScreen.Contains(new Rectangle(p, Size))
        ? p
        : defaultLocation;
    Show();
    Activate();
}
```

The `SystemInformation.VirtualScreen.Contains(...)` check discards a stale saved position if the screen/monitor configuration changed since it was saved (e.g. an external monitor was unplugged), falling back to the default bottom-right anchor rather than opening off-screen.

**TrayAppContext.cs**: subscribes once in the constructor:

```csharp
_flyout.PositionChanged += p =>
{
    _settings.FlyoutX = p.X;
    _settings.FlyoutY = p.Y;
    _settings.Save(Settings.DefaultPath);
};
```

`ToggleFlyout()`'s call site changes from `_flyout.ShowNearTray();` to:

```csharp
_flyout.ShowNearTray(_settings.FlyoutX is int x && _settings.FlyoutY is int y ? new Point(x, y) : null);
```

## Feature 3 — Pin (sticky) toggle

**FlyoutForm.cs** adds:

```csharp
private bool _pinned;
private static readonly Rectangle PinRect = new(312, 8, 20, 20); // top-right corner, 8px from the top/right edges
```

Both icon hit-boxes are fixed 20x20 squares in the header row (`ClientSize.Width` is the fixed 340 from Feature 1, so these are absolute, not computed): `PinRect = (312, 8, 20, 20)` and, from Feature 4, `RefreshRect = (288, 8, 20, 20)` — a 4px gap between them, pin on the right, refresh on its left. Both sit clear of the "Claude Usage" title text drawn at `(16, 12)`.

- Drawn each `OnPaint` using the **Segoe MDL2 Assets** icon font (the standard Windows system-icon font — reliable, no image assets needed, present on every Windows 10/11 install). Glyph codepoint `U+E718` ("Pin") when unpinned, `U+E840` ("PinFill") when pinned, drawn as `((char)0xE718).ToString()` / `((char)0xE840).ToString()` respectively, in `_fore` color, centered in `PinRect`.
- `OnMouseDown`: if the click point is inside `PinRect`, toggle `_pinned` and `Invalidate()`; **do not** start a drag for this click (checked before the drag hit-test in Feature 2's `OnMouseDown`).
- `OnDeactivate`: becomes `if (_pinned) return; base.OnDeactivate(e); Hide();` — i.e. pinned suppresses the auto-hide, nothing else changes. Manual `Hide()` calls (clicking the tray icon while the flyout is open — the existing `ToggleFlyout` "if Visible, Hide()" path) are untouched and still close it regardless of pin state.

No `Settings` changes — pin state resets to unpinned on every launch, by design (Decision #3).

## Feature 4 — Refresh button on the flyout

**FlyoutForm.cs** adds, alongside the pin glyph:

```csharp
private bool _refreshing;
public event Action? RefreshRequested;
```

- Drawn with glyph codepoint `U+E72C` ("Refresh") from Segoe MDL2 Assets, `((char)0xE72C).ToString()`, centered in `RefreshRect = (288, 8, 20, 20)` (immediately left of `PinRect`), in `_fore` when idle and `_dimFore` when `_refreshing` is true.
- `OnMouseDown`: if inside `RefreshRect` **and not already `_refreshing`**, invoke `RefreshRequested?.Invoke()`. A click while `_refreshing` is true is a no-op (ignored, per Decision #4) — checked before the drag hit-test, same as the pin button.
- `public void SetRefreshing(bool value) { _refreshing = value; Invalidate(); }` — called by `TrayAppContext`.

**TrayAppContext.cs**:

```csharp
_flyout.RefreshRequested += async () => await PollAsync();
```

`PollAsync()` gains `_flyout.SetRefreshing(true);` right after the `_polling` re-entrancy check passes (before `await _client.FetchAsync()`), and `_flyout.SetRefreshing(false);` is folded into the existing `finally` block (which already calls `UpdateTray()` → `_flyout.UpdateFrom(...)`; `SetRefreshing(false)` is called right before that so the same repaint picks up both changes).

Because `RefreshRequested` re-enters through the same `PollAsync()` used by the timer tick and the tray menu item, the existing `_polling` guard already prevents an overlapping fetch — the visual dim/ignore-clicks behavior in `FlyoutForm` is a UX nicety on top of that guarantee, not a second correctness mechanism.

## Feature 5 — Stable-GUID tray icon (fixes "Always show")

New file **`src/ClaudeWidget/UI/TrayIcon.cs`**. Public surface intentionally mirrors the subset of `System.Windows.Forms.NotifyIcon` that `TrayAppContext` uses today, so the call-site diff in `TrayAppContext.cs` is mechanical (type swap, not a rewrite of surrounding logic):

```csharp
public sealed class TrayIcon : IDisposable
{
    public Icon? Icon { get; set; }              // setter pushes NIM_MODIFY with NIF_ICON
    public string Text { get; set; } = "";        // setter pushes NIM_MODIFY with NIF_TIP (127-char cap enforced by caller, as today)
    public ContextMenuStrip? ContextMenuStrip { get; set; }
    public event MouseEventHandler? MouseClick;

    public TrayIcon() { /* creates icon (NIM_ADD) with the fixed GUID, hidden message-only window, negotiates NOTIFYICON_VERSION_4 */ }
    public void ShowBalloonTip(int timeoutMs, string title, string text) { /* NIM_MODIFY with NIF_INFO */ }
    public void Dispose() { /* NIM_DELETE, destroy hidden window */ }
}
```

Implementation notes (binding on the plan):

- **Fixed GUID:** a single hardcoded `private static readonly Guid TrayIconGuid = new("7f3c9e1a-4b2d-4e6f-9a1c-3d5e7f9b1c2d");` — generated once for this app and never changed across future releases (changing it would reset every user's pin state once).
- **Hidden window:** subclass `System.Windows.Forms.NativeWindow`; create its handle with `CreateParams { Parent = new IntPtr(-3) /* HWND_MESSAGE */ }` — a lightweight message-only window requiring no visible artifacts and no custom window-class registration.
- **Callback message:** register a private `WM_APP + 1` value as `uCallbackMessage`; override `WndProc` on the hidden window to decode the mouse-event codes Shell posts to it (left click → raise `MouseClick` with `MouseButtons.Left`; right click / `WM_CONTEXTMENU` under v4 behavior → call `SetForegroundWindow` on the hidden window's handle, then `ContextMenuStrip?.Show(Cursor.Position)`, matching the standard WinForms `NotifyIcon` dismiss-on-outside-click trick).
- **Explorer restart resilience:** also handle `RegisterWindowMessage("TaskbarCreated")` in `WndProc` — on receipt, re-issue `NIM_ADD` (Explorer crashing/restarting silently drops all tray icons; this is what makes them reappear without relaunching the app).
- **Version negotiation:** after `NIM_ADD`, send `NIM_SETVERSION` with `uVersion = NOTIFYICON_VERSION_4` (`= 4`) so Shell uses the modern click/overflow behavior (required for consistent "Always show" handling on Windows 10/11).
- **P/Invoke surface** lives entirely inside `TrayIcon.cs` — `Shell_NotifyIconW`, the `NOTIFYICONDATAW` struct, and the message/flag constants are private to this file; nothing outside `UI/TrayIcon.cs` touches P/Invoke.

**TrayAppContext.cs**: `private readonly NotifyIcon _tray;` → `private readonly TrayIcon _tray;`; the two-line `_tray.BalloonTipTitle = ...; _tray.BalloonTipText = ...; _tray.ShowBalloonTip(5000);` sequence collapses to the new three-argument `_tray.ShowBalloonTip(5000, "Claude usage", message);`. `ExitThreadCore`'s `_tray.Visible = false;` line is removed (no `Visible` concept — `Dispose()` alone issues `NIM_DELETE`); `_tray.Icon?.Dispose(); _tray.ContextMenuStrip?.Dispose(); _tray.Dispose();` stays as-is.

## Testing

- **Unit (xUnit), Core only:** `Settings` round-trip test for `FlyoutX`/`FlyoutY` (null by default, persisted/reloaded correctly, independent of the existing `PollIntervalSeconds` clamp).
- **No unit tests for `FlyoutForm` or `TrayIcon`** — both are GDI+/Win32-interop-heavy UI classes with no meaningful way to unit test drag math, glyph hit-testing, or Shell message handling without a live window and message pump. This matches the existing project convention (`FlyoutForm` already has zero unit tests; `TrayAppContext` has zero). Covered instead by an expanded manual smoke-test checklist:
  - Drag the flyout, close and reopen it (same session) — reopens at the dragged position.
  - Fully quit and relaunch the app — flyout still reopens at the dragged position.
  - Unplug/reconfigure a second monitor after dragging the flyout onto it, then reopen — falls back to the default bottom-right position instead of opening off-screen.
  - Pin the flyout, click elsewhere on the desktop — stays open. Click the tray icon — closes. Reopen — starts unpinned again.
  - Click the flyout's refresh glyph — glyph dims immediately, re-enables once the poll completes, values update.
  - Click the flyout's refresh glyph rapidly several times — only one fetch occurs at a time (no overlapping/duplicate toasts).
  - Enable "Always show this icon" for ClaudeWidget in Windows Settings, fully quit and relaunch the app — icon appears directly in the visible tray area, not behind the `^` chevron.
  - Restart `explorer.exe` (or let Windows crash-recover it) while the widget is running — tray icon reappears without relaunching ClaudeWidget.
  - Right-click the tray icon — context menu appears and dismisses correctly on outside click, same as today.

## Out of scope

Multiple simultaneous flyout instances, animating the pin/refresh state transitions, remembering pin state across restarts, remembering per-monitor drag positions (only one saved `(X, Y)` pair, not one per monitor arrangement), any change to the tray icon's own rendering (`IconRenderer` is untouched), true taskbar embedding (ruled out — not permitted by the Windows shell for third-party apps).
