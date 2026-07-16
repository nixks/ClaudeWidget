# Flyout & Tray Icon Additions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add flyout bottom padding, drag-to-reposition (persisted), a pin/sticky toggle, an on-flyout refresh button, and a stable-GUID tray icon that makes Windows' "Always show this icon" setting actually stick.

**Architecture:** All changes are additive to the existing three files (`Settings.cs`, `FlyoutForm.cs`, `TrayAppContext.cs`) plus one new file (`UI/TrayIcon.cs`) that replaces `System.Windows.Forms.NotifyIcon` with a hand-rolled `Shell_NotifyIcon` P/Invoke wrapper carrying a fixed GUID. No changes to `UsageClient`, `UsageParser`, `CredentialsReader`, `ThresholdNotifier`, `Formatting`, `Autostart`, or `IconRenderer`.

**Tech Stack:** .NET 8 (`net8.0-windows`), WinForms, `System.Runtime.InteropServices` P/Invoke (in-box, no new NuGet packages), xUnit.

**Spec:** `docs/superpowers/specs/2026-07-16-widget-flyout-and-tray-additions-design.md`

## Global Constraints

- No new NuGet packages in the app project — `TrayIcon`'s P/Invoke uses only in-box `System.Runtime.InteropServices`, `System.Drawing`, and `System.Windows.Forms` (already available via the existing `net8.0-windows` / `UseWindowsForms` project settings).
- Namespaces unchanged: `ClaudeWidget.Core` for logic, `ClaudeWidget.UI` for icon/flyout/tray, `ClaudeWidget` for wiring.
- Settings file stays camelCase JSON at `%APPDATA%\ClaudeWidget\settings.json`; new fields `flyoutX`/`flyoutY` are nullable and absent/missing deserializes to `null` (no migration needed for existing files).
- Exact flyout icon rects (fixed, `ClientSize.Width` is 340): `PinRect = new Rectangle(312, 8, 20, 20)`, `RefreshRect = new Rectangle(288, 8, 20, 20)`.
- Exact Segoe MDL2 Assets glyph codepoints: Pin = `U+E718`, PinFill = `U+E840`, Refresh = `U+E72C`.
- Fixed tray icon GUID, never change across releases: `7f3c9e1a-4b2d-4e6f-9a1c-3d5e7f9b1c2d`.
- `FlyoutForm` and `TrayIcon` get **no unit tests** — both are GDI+/Win32-interop UI classes with no meaningful way to unit test without a live window/message pump (matches existing project convention: `FlyoutForm` and `TrayAppContext` already have zero unit tests). Verification for tasks touching only these files is `dotnet test` passing (build succeeds, no regression in the existing suite) plus the manual checklist in Task 6.
- Every commit message ends with the trailer line: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Run all tests from repo root with: `dotnet test` — expect PASS before every commit.

---

### Task 1: Settings — remembered flyout position

**Files:**
- Modify: `src/ClaudeWidget/Core/Settings.cs`
- Test: `tests/ClaudeWidget.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Settings.FlyoutX` and `Settings.FlyoutY`, both `int?`, defaulting to `null`, persisted via the existing `Load`/`Save`.

- [ ] **Step 1: Write the failing test**

Add this test method inside the existing `SettingsTests` class in `tests/ClaudeWidget.Tests/SettingsTests.cs` (it already has a `TempPath()` helper — reuse it):

```csharp
[Fact]
public void FlyoutPositionDefaultsToNullAndRoundTrips()
{
    var path = TempPath();
    var defaults = Settings.Load(path);
    Assert.Null(defaults.FlyoutX);
    Assert.Null(defaults.FlyoutY);

    var s = new Settings { FlyoutX = 120, FlyoutY = 340 };
    s.Save(path);
    var loaded = Settings.Load(path);
    Assert.Equal(120, loaded.FlyoutX);
    Assert.Equal(340, loaded.FlyoutY);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL to compile — `Settings` has no `FlyoutX`/`FlyoutY` properties.

- [ ] **Step 3: Add the properties**

In `src/ClaudeWidget/Core/Settings.cs`, add these two properties immediately after the existing `public bool StartWithWindows { get; set; }` line:

```csharp
    public int? FlyoutX { get; set; }
    public int? FlyoutY { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS (all — the existing suite plus this new test).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudeWidget/Core/Settings.cs tests/ClaudeWidget.Tests/SettingsTests.cs
git commit -m "feat: add nullable flyout position to Settings"
```

---

### Task 2: FlyoutForm — bottom padding and drag-to-reposition

**Files:**
- Modify: `src/ClaudeWidget/UI/FlyoutForm.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `public event Action<Point>? PositionChanged;` (fired with the final `Location` when a drag ends); `public void ShowNearTray(Point? savedPosition)` (replaces the old parameterless `ShowNearTray()`).

- [ ] **Step 1: Grow the client size by 10px**

In the `FlyoutForm` constructor, change:

```csharp
        ClientSize = new Size(340, 160);
```

to:

```csharp
        ClientSize = new Size(340, 170);
```

- [ ] **Step 2: Add drag-tracking fields and the position-changed event**

Add these fields near the top of the class, alongside the existing `_snapshot`/`_stale`/`_authError` fields:

```csharp
    private bool _dragging;
    private Point _dragStartScreenPoint;
    private Point _dragStartFormLocation;

    public event Action<Point>? PositionChanged;
```

- [ ] **Step 3: Replace `ShowNearTray()` with a version that accepts a saved position**

Replace the existing method:

```csharp
    public void ShowNearTray()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 8, area.Bottom - Height - 8);
        Show();
        Activate();
    }
```

with:

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

- [ ] **Step 4: Add mouse handlers for dragging**

Add these three method overrides to the class (anywhere after the constructor, e.g. right before `OnDeactivate`):

```csharp
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStartScreenPoint = Cursor.Position;
        _dragStartFormLocation = Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var current = Cursor.Position;
        Location = new Point(
            _dragStartFormLocation.X + (current.X - _dragStartScreenPoint.X),
            _dragStartFormLocation.Y + (current.Y - _dragStartScreenPoint.Y));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        PositionChanged?.Invoke(Location);
    }
```

- [ ] **Step 5: Verify the build (no unit tests for this file, per Global Constraints)**

Run: `dotnet test`
Expected: build succeeds, all existing tests PASS (the project won't compile yet against `TrayAppContext.cs`'s old `ShowNearTray()` call — that call site is fixed in Task 6; `FlyoutForm.cs` itself and the test project both compile independently of `TrayAppContext.cs`, so this step's `dotnet test` run validates the test project, which does not reference `TrayAppContext`. If the build fails here, `dotnet build src/ClaudeWidget/ClaudeWidget.csproj` will additionally show the expected, temporary `TrayAppContext.cs` call-site error — that is expected until Task 6 and is not a defect in this task).

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeWidget/UI/FlyoutForm.cs
git commit -m "feat: add flyout bottom padding and drag-to-reposition"
```

---

### Task 3: FlyoutForm — pin (sticky) toggle

**Files:**
- Modify: `src/ClaudeWidget/UI/FlyoutForm.cs`

**Interfaces:**
- Consumes: `OnMouseDown` from Task 2 (this task inserts a guard clause at the top of it).
- Produces: pinned flyout no longer auto-hides on deactivate.

- [ ] **Step 1: Add the pinned-state field and hit-box rect**

Add near the other fields:

```csharp
    private bool _pinned;
    private static readonly Rectangle PinRect = new(312, 8, 20, 20);
```

- [ ] **Step 2: Handle the pin click in `OnMouseDown`, before the drag starts**

Replace the `OnMouseDown` body added in Task 2:

```csharp
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStartScreenPoint = Cursor.Position;
        _dragStartFormLocation = Location;
    }
```

with:

```csharp
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (PinRect.Contains(e.Location))
        {
            _pinned = !_pinned;
            Invalidate();
            return;
        }
        _dragging = true;
        _dragStartScreenPoint = Cursor.Position;
        _dragStartFormLocation = Location;
    }
```

- [ ] **Step 3: Suppress auto-hide while pinned**

Replace:

```csharp
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }
```

with:

```csharp
    protected override void OnDeactivate(EventArgs e)
    {
        if (_pinned) return;
        base.OnDeactivate(e);
        Hide();
    }
```

- [ ] **Step 4: Draw the pin glyph**

In `OnPaint`, immediately after the line `g.DrawString("Claude Usage", titleFont, fore, 16, 12);`, insert:

```csharp
        using var iconFont = new Font("Segoe MDL2 Assets", 11f);
        using var iconFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var pinGlyph = _pinned ? ((char)0xE840).ToString() : ((char)0xE718).ToString();
        g.DrawString(pinGlyph, iconFont, fore, PinRect, iconFormat);
```

- [ ] **Step 5: Verify the build**

Run: `dotnet test`
Expected: build succeeds (test project compiles cleanly), all existing tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeWidget/UI/FlyoutForm.cs
git commit -m "feat: add pin toggle to keep the flyout open"
```

---

### Task 4: FlyoutForm — refresh button

**Files:**
- Modify: `src/ClaudeWidget/UI/FlyoutForm.cs`

**Interfaces:**
- Consumes: `OnMouseDown` and the `iconFont`/`iconFormat` locals from Task 3's `OnPaint` edit.
- Produces: `public event Action? RefreshRequested;`, `public void SetRefreshing(bool value)`.

- [ ] **Step 1: Add the refreshing-state field, event, and hit-box rect**

Add near the other fields:

```csharp
    private bool _refreshing;
    private static readonly Rectangle RefreshRect = new(288, 8, 20, 20);

    public event Action? RefreshRequested;
```

- [ ] **Step 2: Handle the refresh click in `OnMouseDown`, checked before the pin check**

Replace the `OnMouseDown` body from Task 3:

```csharp
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (PinRect.Contains(e.Location))
        {
            _pinned = !_pinned;
            Invalidate();
            return;
        }
        _dragging = true;
        _dragStartScreenPoint = Cursor.Position;
        _dragStartFormLocation = Location;
    }
```

with:

```csharp
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (RefreshRect.Contains(e.Location))
        {
            if (!_refreshing) RefreshRequested?.Invoke();
            return;
        }
        if (PinRect.Contains(e.Location))
        {
            _pinned = !_pinned;
            Invalidate();
            return;
        }
        _dragging = true;
        _dragStartScreenPoint = Cursor.Position;
        _dragStartFormLocation = Location;
    }
```

- [ ] **Step 3: Add `SetRefreshing`**

Add this public method (e.g. right after `UpdateFrom`):

```csharp
    public void SetRefreshing(bool value)
    {
        _refreshing = value;
        Invalidate();
    }
```

- [ ] **Step 4: Draw the refresh glyph**

In `OnPaint`, immediately after the pin-glyph line added in Task 3 (`g.DrawString(pinGlyph, iconFont, fore, PinRect, iconFormat);`), insert:

```csharp
        var refreshColor = _refreshing ? dim : fore;
        g.DrawString(((char)0xE72C).ToString(), iconFont, refreshColor, RefreshRect, iconFormat);
```

- [ ] **Step 5: Verify the build**

Run: `dotnet test`
Expected: build succeeds, all existing tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeWidget/UI/FlyoutForm.cs
git commit -m "feat: add refresh button to the flyout"
```

---

### Task 5: TrayIcon — stable-GUID Shell_NotifyIcon wrapper

**Files:**
- Create: `src/ClaudeWidget/UI/TrayIcon.cs`

**Interfaces:**
- Consumes: nothing from this plan's other tasks.
- Produces: `public sealed class TrayIcon : IDisposable` in `ClaudeWidget.UI`, with:
  - `public Icon? Icon { get; set; }`
  - `public string Text { get; set; }`
  - `public ContextMenuStrip? ContextMenuStrip { get; set; }`
  - `public event MouseEventHandler? MouseClick;`
  - `public TrayIcon()`
  - `public void ShowBalloonTip(int timeoutMs, string title, string text)`
  - `public void Dispose()`

- [ ] **Step 1: Write the complete file**

Create `src/ClaudeWidget/UI/TrayIcon.cs`:

```csharp
using System.Drawing;
using System.Runtime.InteropServices;

namespace ClaudeWidget.UI;

public sealed class TrayIcon : IDisposable
{
    private static readonly Guid TrayIconGuid = new("7f3c9e1a-4b2d-4e6f-9a1c-3d5e7f9b1c2d");
    private static readonly int TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;
    private const int NIF_GUID = 0x00000020;
    private const int NIF_SHOWTIP = 0x00000080;

    private const int NOTIFYICON_VERSION_4 = 4;

    private const int WM_APP = 0x8000;
    private const int TrayCallbackMessage = WM_APP + 1;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessageW(string lpString);

    private sealed class TrayNativeWindow : NativeWindow
    {
        public event Action<Message>? MessageReceived;

        public TrayNativeWindow()
        {
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        protected override void WndProc(ref Message m)
        {
            MessageReceived?.Invoke(m);
            base.WndProc(ref m);
        }
    }

    private readonly TrayNativeWindow _window = new();
    private Icon? _icon;
    private string _text = "";
    private bool _added;

    public ContextMenuStrip? ContextMenuStrip { get; set; }
    public event MouseEventHandler? MouseClick;

    public Icon? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            if (_added) Modify(NIF_ICON);
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            if (_added) Modify(NIF_TIP | NIF_SHOWTIP);
        }
    }

    public TrayIcon()
    {
        _window.MessageReceived += OnMessage;
        Add();
    }

    private NOTIFYICONDATAW BuildData(int flags)
    {
        return new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon?.Handle ?? IntPtr.Zero,
            szTip = _text,
            szInfo = "",
            szInfoTitle = "",
            guidItem = TrayIconGuid,
        };
    }

    private void Add()
    {
        var data = BuildData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP | NIF_GUID);
        Shell_NotifyIconW(NIM_ADD, ref data);
        _added = true;

        var versionData = BuildData(NIF_GUID);
        versionData.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref versionData);
    }

    private void Modify(int flags)
    {
        var data = BuildData(flags | NIF_GUID);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void ShowBalloonTip(int timeoutMs, string title, string text)
    {
        var data = BuildData(NIF_INFO | NIF_GUID);
        data.szInfo = text;
        data.szInfoTitle = title;
        data.uTimeoutOrVersion = timeoutMs;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void OnMessage(Message m)
    {
        if (m.Msg == TaskbarCreatedMessage)
        {
            Add();
            return;
        }
        if (m.Msg != TrayCallbackMessage) return;

        var evt = (int)(m.LParam.ToInt64() & 0xFFFF);
        if (evt == WM_LBUTTONUP)
        {
            MouseClick?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 1, Cursor.Position.X, Cursor.Position.Y, 0));
        }
        else if (evt == WM_RBUTTONUP || evt == WM_CONTEXTMENU)
        {
            SetForegroundWindow(_window.Handle);
            ContextMenuStrip?.Show(Cursor.Position);
        }
    }

    public void Dispose()
    {
        var data = BuildData(NIF_GUID);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _window.DestroyHandle();
    }
}
```

- [ ] **Step 2: Verify the build**

Run: `dotnet test`
Expected: build succeeds (this file compiles standalone; nothing references it yet), all existing tests PASS.

- [ ] **Step 3: Commit**

```bash
git add src/ClaudeWidget/UI/TrayIcon.cs
git commit -m "feat: add Shell_NotifyIcon wrapper with stable GUID for tray pinning"
```

---

### Task 6: TrayAppContext — wire it all together

**Files:**
- Modify: `src/ClaudeWidget/TrayAppContext.cs`

**Interfaces:**
- Consumes: `Settings.FlyoutX`/`FlyoutY` (Task 1); `FlyoutForm.PositionChanged`, `FlyoutForm.ShowNearTray(Point?)`, `FlyoutForm.RefreshRequested`, `FlyoutForm.SetRefreshing(bool)` (Tasks 2 & 4); `TrayIcon` (Task 5).
- Produces: a fully wired, runnable tray app with all five additions active.

- [ ] **Step 1: Swap `NotifyIcon` for `TrayIcon`**

Change the field declaration:

```csharp
    private readonly NotifyIcon _tray;
```

to:

```csharp
    private readonly TrayIcon _tray;
```

- [ ] **Step 2: Update tray construction**

Replace:

```csharp
        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleFlyout();
        };
        UpdateTray();
```

with:

```csharp
        _tray = new TrayIcon { ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleFlyout();
        };
        _flyout.PositionChanged += p =>
        {
            _settings.FlyoutX = p.X;
            _settings.FlyoutY = p.Y;
            _settings.Save(Settings.DefaultPath);
        };
        _flyout.RefreshRequested += async () => await PollAsync();
        UpdateTray();
```

- [ ] **Step 3: Set/clear the refreshing flag around each poll**

Replace:

```csharp
    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var result = await _client.FetchAsync();
```

with:

```csharp
    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        _flyout.SetRefreshing(true);
        try
        {
            var result = await _client.FetchAsync();
```

and replace:

```csharp
        finally
        {
            _polling = false;
            UpdateTray();
        }
    }
```

with:

```csharp
        finally
        {
            _polling = false;
            _flyout.SetRefreshing(false);
            UpdateTray();
        }
    }
```

- [ ] **Step 4: Use the new 3-argument `ShowBalloonTip`**

Replace:

```csharp
                    if (message is not null)
                    {
                        _tray.BalloonTipTitle = "Claude usage";
                        _tray.BalloonTipText = message;
                        _tray.ShowBalloonTip(5000);
                    }
```

with:

```csharp
                    if (message is not null)
                    {
                        _tray.ShowBalloonTip(5000, "Claude usage", message);
                    }
```

- [ ] **Step 5: Pass the saved position into `ShowNearTray`**

Replace:

```csharp
    private void ToggleFlyout()
    {
        if (_flyout.Visible)
        {
            _flyout.Hide();
        }
        else
        {
            _flyout.UpdateFrom(_snapshot, IsStale, _authError);
            _flyout.ShowNearTray();
        }
    }
```

with:

```csharp
    private void ToggleFlyout()
    {
        if (_flyout.Visible)
        {
            _flyout.Hide();
        }
        else
        {
            _flyout.UpdateFrom(_snapshot, IsStale, _authError);
            _flyout.ShowNearTray(_settings.FlyoutX is int x && _settings.FlyoutY is int y ? new Point(x, y) : null);
        }
    }
```

- [ ] **Step 6: Drop the `Visible` assignment in `ExitThreadCore`**

Replace:

```csharp
    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Icon?.Dispose();
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _timer.Dispose();
        _flyout.Dispose();
        base.ExitThreadCore();
    }
```

with:

```csharp
    protected override void ExitThreadCore()
    {
        _tray.Icon?.Dispose();
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _timer.Dispose();
        _flyout.Dispose();
        base.ExitThreadCore();
    }
```

- [ ] **Step 7: Verify the full build and test suite**

Run: `dotnet test`
Expected: build succeeds cleanly (no more references to the old `NotifyIcon`-only members or the parameterless `ShowNearTray()`), all existing tests PASS.

- [ ] **Step 8: Publish and manually smoke-test**

Run:
```powershell
dotnet publish src/ClaudeWidget/ClaudeWidget.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
Start-Process publish\ClaudeWidget.exe
```

Automated/process-level check: confirm the process is still running after ~10 seconds (`Get-Process ClaudeWidget -ErrorAction SilentlyContinue`) and stop it with `Stop-Process -Name ClaudeWidget` when done — do not click any UI from this automated step.

The following require a human at the keyboard and are **deferred to the user** (not claimed as passed by an automated run):
- Left-click the tray icon: flyout opens 10px taller with visible bottom padding.
- Drag the flyout to a new spot, close and reopen it (same run) — reopens at the dragged spot.
- Quit and relaunch the app — flyout still reopens at the dragged spot.
- Unplug/reconfigure a second monitor after dragging the flyout onto it, then reopen — falls back to the default bottom-right position instead of opening off-screen.
- Click the pin glyph — flyout stays open when clicking elsewhere on the desktop; click it again to unpin, then click away — it closes. Relaunch — starts unpinned again.
- Click the refresh glyph — it dims immediately, re-enables once the poll completes, values update; rapid repeated clicks don't cause overlapping fetches or duplicate toasts.
- In Windows Settings → Personalization → Taskbar → "Select which icons appear on the taskbar", switch ClaudeWidget to "On". Quit and relaunch the app — the icon now appears directly in the visible tray area instead of behind the `^` overflow chevron.
- Restart `explorer.exe` (or let Windows crash-recover it) while the widget is running — the tray icon reappears without relaunching ClaudeWidget.
- Right-click the tray icon — context menu appears and dismisses correctly on an outside click, same as before this change.

- [ ] **Step 9: Commit**

```bash
git add src/ClaudeWidget/TrayAppContext.cs
git commit -m "feat: wire drag/pin/refresh into the flyout and switch to the stable-GUID tray icon"
```
