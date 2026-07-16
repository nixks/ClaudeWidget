using System.Drawing;
using System.Drawing.Drawing2D;
using ClaudeWidget.Core;
using Microsoft.Win32;

namespace ClaudeWidget.UI;

public sealed class FlyoutForm : Form
{
    private UsageSnapshot? _snapshot;
    private bool _stale;
    private bool _authError;

    private bool _dragging;
    private Point _dragStartScreenPoint;
    private Point _dragStartFormLocation;

    private bool _pinned;
    private static readonly Rectangle PinRect = new(312, 8, 20, 20);

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
        ClientSize = new Size(340, 170);
        BackColor = _back;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => false;

    public void UpdateFrom(UsageSnapshot? snapshot, bool stale, bool authError)
    {
        _snapshot = snapshot;
        _stale = stale;
        _authError = authError;
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
        if (_pinned) return;
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var titleFont = new Font("Segoe UI Semibold", 11f);
        using var font = new Font("Segoe UI", 9.5f);
        using var smallFont = new Font("Segoe UI", 8.5f);
        using var fore = new SolidBrush(_fore);
        using var dim = new SolidBrush(_dimFore);
        using var borderPen = new Pen(_border);

        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        g.DrawString("Claude Usage", titleFont, fore, 16, 12);

        using var iconFont = new Font("Segoe MDL2 Assets", 11f);
        using var iconFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var pinGlyph = _pinned ? ((char)0xE840).ToString() : ((char)0xE718).ToString();
        g.DrawString(pinGlyph, iconFont, fore, PinRect, iconFormat);

        if (_authError)
        {
            g.DrawString("Not signed in — Claude Code credentials not found.", font, fore, 16, 52);
            g.DrawString("Log in with Claude Code, then click Refresh now.", font, dim, 16, 74);
            return;
        }
        if (_snapshot is null)
        {
            g.DrawString("Waiting for first update…", font, fore, 16, 52);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        DrawRow(g, "Session", _snapshot.SessionPercent,
            $"resets in {Formatting.Countdown(_snapshot.SessionResetsAtUtc, now)}",
            48, font, smallFont, fore, dim);
        DrawRow(g, "Weekly", _snapshot.WeeklyPercent,
            $"resets {Formatting.AbsoluteLocal(_snapshot.WeeklyResetsAtUtc)}",
            92, font, smallFont, fore, dim);

        var source = _snapshot.Source == UsageSource.UsageEndpoint ? "usage endpoint" : "probe";
        var staleTag = _stale ? " · STALE" : "";
        g.DrawString(
            $"Updated {_snapshot.FetchedAtUtc.ToLocalTime():HH:mm:ss} · {source}{staleTag}",
            smallFont, dim, 16, 138);
    }

    private void DrawRow(Graphics g, string label, double? percent, string resetText, int y,
        Font font, Font smallFont, Brush fore, Brush dim)
    {
        g.DrawString(label, font, fore, 16, y);
        var bar = new Rectangle(84, y + 4, 150, 12);
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
            g.DrawString($"{percent:0}%", font, fore, 242, y);
        }
        else
        {
            g.DrawString("—", font, fore, 242, y);
        }
        g.DrawString(resetText, smallFont, dim, 84, y + 20);
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
