using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class UsageParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParsesFullResponseWithPercentScale()
    {
        var json = """
            {"five_hour":{"utilization":42,"resets_at":"2026-07-16T18:00:00Z"},
             "seven_day":{"utilization":31,"resets_at":"2026-07-21T08:00:00Z"}}
            """;
        var s = UsageParser.ParseUsageJson(json, Now);
        Assert.NotNull(s);
        Assert.Equal(42, s!.SessionPercent);
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 18, 0, 0, TimeSpan.Zero), s.SessionResetsAtUtc);
        Assert.Equal(31, s.WeeklyPercent);
        Assert.Equal(new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.Zero), s.WeeklyResetsAtUtc);
        Assert.Equal(Now, s.FetchedAtUtc);
        Assert.Equal(UsageSource.UsageEndpoint, s.Source);
    }

    [Fact]
    public void NormalizesFractionScale()
    {
        var json = """{"five_hour":{"utilization":0.42},"seven_day":{"utilization":0.31}}""";
        var s = UsageParser.ParseUsageJson(json, Now);
        Assert.Equal(42, s!.SessionPercent, precision: 5);
        Assert.Equal(31, s.WeeklyPercent!.Value, precision: 5);
    }

    [Fact]
    public void ParsesUnixNumberResetTimestamp()
    {
        var json = """{"five_hour":{"utilization":10,"resets_at":1784224800}}""";
        var s = UsageParser.ParseUsageJson(json, Now);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784224800), s!.SessionResetsAtUtc);
    }

    [Fact]
    public void MissingSevenDayGivesNullWeekly()
    {
        var json = """{"five_hour":{"utilization":10}}""";
        var s = UsageParser.ParseUsageJson(json, Now);
        Assert.NotNull(s);
        Assert.Null(s!.WeeklyPercent);
        Assert.Null(s.WeeklyResetsAtUtc);
    }

    [Fact]
    public void MissingFiveHourReturnsNull()
        => Assert.Null(UsageParser.ParseUsageJson("""{"seven_day":{"utilization":10}}""", Now));

    [Fact]
    public void GarbageReturnsNull()
    {
        Assert.Null(UsageParser.ParseUsageJson("not json at all", Now));
        Assert.Null(UsageParser.ParseUsageJson("""{"five_hour":{"utilization":"high"}}""", Now));
    }

    [Theory]
    [InlineData(0.5, 50)]
    [InlineData(1.0, 100)]
    [InlineData(42, 42)]
    [InlineData(0, 0)]
    public void NormalizePercentRule(double raw, double expected)
        => Assert.Equal(expected, UsageParser.NormalizePercent(raw));
}
