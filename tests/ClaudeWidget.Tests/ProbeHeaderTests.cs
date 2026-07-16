using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class ProbeHeaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private static Func<string, string?> Headers(Dictionary<string, string> d)
        => name => d.TryGetValue(name, out var v) ? v : null;

    [Fact]
    public void ParsesAllFourHeaders()
    {
        var h = Headers(new()
        {
            ["anthropic-ratelimit-unified-5h-utilization"] = "0.42",
            ["anthropic-ratelimit-unified-5h-reset"] = "1784224800",
            ["anthropic-ratelimit-unified-7d-utilization"] = "0.31",
            ["anthropic-ratelimit-unified-7d-reset"] = "1784656800",
        });
        var s = UsageParser.ParseProbeHeaders(h, Now);
        Assert.NotNull(s);
        Assert.Equal(42, s!.SessionPercent, precision: 5);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784224800), s.SessionResetsAtUtc);
        Assert.Equal(31, s.WeeklyPercent!.Value, precision: 5);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784656800), s.WeeklyResetsAtUtc);
        Assert.Equal(UsageSource.Probe, s.Source);
    }

    [Fact]
    public void ParsesIsoResetTimestamp()
    {
        var h = Headers(new()
        {
            ["anthropic-ratelimit-unified-5h-utilization"] = "0.1",
            ["anthropic-ratelimit-unified-5h-reset"] = "2026-07-16T18:00:00Z",
        });
        var s = UsageParser.ParseProbeHeaders(h, Now);
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 18, 0, 0, TimeSpan.Zero), s!.SessionResetsAtUtc);
    }

    [Fact]
    public void MissingSessionUtilizationReturnsNull()
        => Assert.Null(UsageParser.ParseProbeHeaders(Headers(new()), Now));

    [Fact]
    public void MissingWeeklyHeadersGiveNullWeekly()
    {
        var h = Headers(new() { ["anthropic-ratelimit-unified-5h-utilization"] = "0.5" });
        var s = UsageParser.ParseProbeHeaders(h, Now);
        Assert.NotNull(s);
        Assert.Null(s!.WeeklyPercent);
        Assert.Null(s.SessionResetsAtUtc);
    }
}
