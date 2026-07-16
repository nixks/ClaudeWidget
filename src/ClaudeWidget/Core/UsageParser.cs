using System.Globalization;
using System.Text.Json;

namespace ClaudeWidget.Core;

public static class UsageParser
{
    public static UsageSnapshot? ParseUsageJson(string json, DateTimeOffset nowUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!TryGetWindow(root, "five_hour", out var sessionPct, out var sessionReset)) return null;
            double? weeklyPct = null;
            DateTimeOffset? weeklyReset = null;
            if (TryGetWindow(root, "seven_day", out var wp, out var wr))
            {
                weeklyPct = wp;
                weeklyReset = wr;
            }
            return new UsageSnapshot(sessionPct, sessionReset, weeklyPct, weeklyReset, nowUtc, UsageSource.UsageEndpoint);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static double NormalizePercent(double raw) => raw <= 1.0 ? raw * 100.0 : raw;

    public static UsageSnapshot? ParseProbeHeaders(Func<string, string?> header, DateTimeOffset nowUtc)
    {
        var sessionUtil = ParseInvariantDouble(header("anthropic-ratelimit-unified-5h-utilization"));
        if (sessionUtil is null) return null;
        var sessionReset = ParseResetValue(header("anthropic-ratelimit-unified-5h-reset"));
        var weeklyUtil = ParseInvariantDouble(header("anthropic-ratelimit-unified-7d-utilization"));
        var weeklyReset = ParseResetValue(header("anthropic-ratelimit-unified-7d-reset"));
        return new UsageSnapshot(
            NormalizePercent(sessionUtil.Value),
            sessionReset,
            weeklyUtil is null ? null : NormalizePercent(weeklyUtil.Value),
            weeklyReset,
            nowUtc,
            UsageSource.Probe);
    }

    private static double? ParseInvariantDouble(string? s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseResetValue(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
            return DateTimeOffset.FromUnixTimeSeconds(unix);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;
    }

    private static bool TryGetWindow(JsonElement root, string name, out double percent, out DateTimeOffset? resetsAt)
    {
        percent = 0;
        resetsAt = null;
        if (!root.TryGetProperty(name, out var win) || win.ValueKind != JsonValueKind.Object) return false;
        if (!win.TryGetProperty("utilization", out var util) || util.ValueKind != JsonValueKind.Number) return false;
        percent = NormalizePercent(util.GetDouble());
        if (win.TryGetProperty("resets_at", out var reset))
        {
            if (reset.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dto))
            {
                resetsAt = dto;
            }
            else if (reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var unix))
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(unix);
            }
        }
        return true;
    }
}
