using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeWidget.Core;
using Microsoft.Win32;

namespace ClaudeWidget.UI;

public sealed class FlyoutForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMoveSizeActivate = 0x0001 | 0x0002 | 0x0010;

    // A click on this TopMost borderless window can trigger a spurious OnDeactivate right
    // before the click itself is delivered (observed specifically on the pin/refresh buttons,
    // which sit close to the screen edge where the flyout is anchored). Hiding synchronously
    // on every deactivation closed the flyout before its own click handler ever ran. Debounce
    // by re-checking who actually holds the foreground a moment later.
    private readonly System.Windows.Forms.Timer _hideDebounceTimer = new() { Interval = 150 };

    private UsageSnapshot? _snapshot;
    private bool _stale;
    private bool _authError;

    private bool _dragging;
    private Point _dragStartScreenPoint;
    private Point _dragStartFormLocation;

    private const int BoxPadding = 16;

    private static readonly Color PinActiveColor = Color.FromArgb(59, 130, 246);

    private bool _pinned;
    private static readonly Rectangle PinRect = new(322, 17, 22, 22);

    private bool _refreshing;
    private static readonly Rectangle RefreshRect = new(288, 17, 22, 22);

    public event Action? RefreshRequested;
    public event Action<Point>? PositionChanged;

    private readonly Color _back;
    private readonly Color _fore;
    private readonly Color _dimFore;
    private readonly Color _barBack;
    private readonly Color _border;

    public FlyoutForm()
    {
        var dark = IsDarkMode();
        _back = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        _fore = dark ? Color.White : Color.FromArgb(27, 27, 27);
        _dimFore = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
        _barBack = dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(217, 217, 217);
        _border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(190, 190, 190);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(360, 210);
        BackColor = _back;
        DoubleBuffered = true;

        _hideDebounceTimer.Tick += (_, _) =>
        {
            _hideDebounceTimer.Stop();
            if (!_pinned && GetForegroundWindow() != Handle) Hide();
        };
    }

    protected override bool ShowWithoutActivation => false;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hideDebounceTimer.Dispose();
        base.Dispose(disposing);
    }

    public void UpdateFrom(UsageSnapshot? snapshot, bool stale, bool authError)
    {
        _snapshot = snapshot;
        _stale = stale;
        _authError = authError;
        Invalidate();
    }

    public void SetRefreshing(bool value)
    {
        _refreshing = value;
        Invalidate();
    }

    public void ShowNearTray(Point? savedPosition = null)
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        var defaultLocation = new Point(area.Right - Width - 8, area.Bottom - Height - 8);
        Location = savedPosition is { } p && SystemInformation.VirtualScreen.Contains(new Rectangle(p, Size))
            ? p
            : defaultLocation;
        Show();
        Activate();
    }

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
            if (_pinned) _hideDebounceTimer.Stop();
            Invalidate();
            return;
        }
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

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_pinned)
        {
            // TopMost=true only guarantees staying above non-topmost windows; the OS still
            // drops an unfocused topmost window behind other topmost/foreground windows once
            // it loses activation. Re-assert the frontmost position without stealing focus
            // back (SWP_NOACTIVATE), so a pinned flyout actually stays visible on top.
            SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMoveSizeActivate);
            return;
        }
        _hideDebounceTimer.Stop();
        _hideDebounceTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font("Segoe UI Semibold", 11f);
        using var font = new Font("Segoe UI", 9.5f);
        using var smallFont = new Font("Segoe UI", 8.5f);
        using var fore = new SolidBrush(_fore);
        using var dim = new SolidBrush(_dimFore);
        using var borderPen = new Pen(_border);

        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        g.DrawString("Claude Usage", titleFont, fore, BoxPadding, 14);

        using var iconFont = new Font(IconFontFamily, 12f);
        // NoClip: the icon font's natural line height is taller than the 22px hit-test box,
        // and DrawString otherwise clips glyphs to their layout rectangle - cropping a
        // sliver off the top/bottom of the pin and refresh glyphs.
        using var iconFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoClip,
        };
        // The pin/unpin glyphs are too close in shape to read as a state change at 12pt, so
        // color carries the "is it pinned" signal instead of relying on the glyph alone.
        var pinGlyph = _pinned ? ((char)0xE840).ToString() : ((char)0xE718).ToString();
        using var pinBrush = new SolidBrush(_pinned ? PinActiveColor : _fore);
        g.DrawString(pinGlyph, iconFont, pinBrush, PinRect, iconFormat);

        var refreshColor = _refreshing ? dim : fore;
        g.DrawString(((char)0xE72C).ToString(), iconFont, refreshColor, RefreshRect, iconFormat);

        var textWidth = Width - BoxPadding * 2;
        if (_authError)
        {
            g.DrawString("Not signed in — Claude Code credentials not found.", font, fore,
                new RectangleF(BoxPadding, 56, textWidth, 40));
            g.DrawString("Log in with Claude Code, then click Refresh now.", font, dim,
                new RectangleF(BoxPadding, 96, textWidth, 40));
            return;
        }
        if (_snapshot is null)
        {
            g.DrawString("Waiting for first update…", font, fore, new RectangleF(BoxPadding, 56, textWidth, 40));
            return;
        }

        var now = DateTimeOffset.UtcNow;
        DrawRow(g, "Session", _snapshot.SessionPercent,
            $"resets in {Formatting.Countdown(_snapshot.SessionResetsAtUtc, now)}",
            56, font, smallFont, fore, dim);
        DrawRow(g, "Weekly", _snapshot.WeeklyPercent,
            $"resets {Formatting.AbsoluteLocal(_snapshot.WeeklyResetsAtUtc)}",
            116, font, smallFont, fore, dim);

        var source = _snapshot.Source == UsageSource.UsageEndpoint ? "usage endpoint" : "probe";
        var staleTag = _stale ? " · STALE" : "";
        g.DrawString(
            $"Updated {_snapshot.FetchedAtUtc.ToLocalTime():HH:mm:ss} · {source}{staleTag}",
            smallFont, dim, BoxPadding, 176);
    }

    private void DrawRow(Graphics g, string label, double? percent, string resetText, int y,
        Font font, Font smallFont, Brush fore, Brush dim)
    {
        g.DrawString(label, font, fore, BoxPadding, y);
        const int barX = 100;
        const int barWidth = 136;
        const int percentX = barX + barWidth + 4;
        var bar = new Rectangle(barX, y + 10, barWidth, 14);
        using (var back = new SolidBrush(_barBack))
        {
            g.FillRectangle(back, bar);
        }
        if (percent is not null)
        {
            var width = (int)(bar.Width * Math.Clamp(percent.Value, 0, 100) / 100.0);
            if (width > 0)
            {
                using var fill = new SolidBrush(IconRenderer.ColorFor(percent, stale: false, authError: false));
                g.FillRectangle(fill, new Rectangle(bar.X, bar.Y, width, bar.Height));
            }
            g.DrawString($"{percent:0}%", font, fore, percentX, y);
        }
        else
        {
            g.DrawString("—", font, fore, percentX, y);
        }
        g.DrawString(resetText, smallFont, dim, barX, y + 29);
    }

    private static readonly string IconFontFamily = ResolveIconFontFamily();

    private static string ResolveIconFontFamily()
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Contains("Segoe Fluent Icons")) return "Segoe Fluent Icons";
        if (names.Contains("Segoe MDL2 Assets")) return "Segoe MDL2 Assets";
        return "Segoe UI";
    }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}
