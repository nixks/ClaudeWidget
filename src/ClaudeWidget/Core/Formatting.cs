using System.Globalization;

namespace ClaudeWidget.Core;

public static class Formatting
{
    public static string Countdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null) return "—";
        var delta = resetsAt.Value - now;
        if (delta <= TimeSpan.Zero) return "now";
        return delta.TotalHours >= 1
            ? $"{(int)delta.TotalHours}h {delta.Minutes:00}m"
            : $"{Math.Max(1, (int)Math.Ceiling(delta.TotalMinutes))}m";
    }

    public static string AbsoluteLocal(DateTimeOffset? resetsAt)
        => resetsAt?.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture) ?? "—";

    public static string Tooltip(UsageSnapshot? s, bool stale, bool authError, DateTimeOffset now)
    {
        if (authError) return "Claude — not signed in (credentials not found)";
        if (s is null) return "Claude — waiting for first update…";
        var weekly = s.WeeklyPercent is null ? "" : $" · wk {s.WeeklyPercent:0}%";
        var staleTag = stale ? " (stale)" : "";
        return $"Claude — session {s.SessionPercent:0}%, resets in {Countdown(s.SessionResetsAtUtc, now)}{weekly}{staleTag}";
    }
}
