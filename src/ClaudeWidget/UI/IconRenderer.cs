using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeWidget.UI;

public static class IconRenderer
{
    public static readonly Color Green = Color.FromArgb(22, 163, 74);
    public static readonly Color Amber = Color.FromArgb(217, 119, 6);
    public static readonly Color Red = Color.FromArgb(220, 38, 38);
    public static readonly Color Gray = Color.FromArgb(110, 110, 110);

    public static Color ColorFor(double? sessionPercent, bool stale, bool authError)
    {
        if (authError || stale || sessionPercent is null) return Gray;
        return sessionPercent < 70 ? Green : sessionPercent < 90 ? Amber : Red;
    }

    public static string IconText(double? sessionPercent, bool authError)
    {
        if (authError) return "!";
        if (sessionPercent is null) return "?";
        var rounded = Math.Round(sessionPercent.Value);
        return rounded >= 100 ? "!!" : rounded.ToString("0");
    }

    public static Icon Render(string text, Color background)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var path = RoundedRect(new Rectangle(0, 0, size - 1, size - 1), 9))
            using (var brush = new SolidBrush(background))
            {
                g.FillPath(brush, path);
            }
            using var font = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 1, size, size), format);
        }
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
