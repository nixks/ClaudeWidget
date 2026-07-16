using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class FormattingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CountdownHoursAndMinutes()
        => Assert.Equal("2h 13m", Formatting.Countdown(Now.AddHours(2).AddMinutes(13), Now));

    [Fact]
    public void CountdownMinutesOnly()
        => Assert.Equal("45m", Formatting.Countdown(Now.AddMinutes(45), Now));

    [Fact]
    public void CountdownPastIsNow()
        => Assert.Equal("now", Formatting.Countdown(Now.AddMinutes(-1), Now));

    [Fact]
    public void CountdownNullIsDash()
        => Assert.Equal("—", Formatting.Countdown(null, Now));

    [Fact]
    public void TooltipNormal()
    {
        var s = new UsageSnapshot(42, Now.AddHours(2).AddMinutes(13), 31, null, Now, UsageSource.UsageEndpoint);
        var tip = Formatting.Tooltip(s, stale: false, authError: false, Now);
        Assert.Equal("Claude — session 42%, resets in 2h 13m · wk 31%", tip);
        Assert.True(tip.Length <= 127);
    }

    [Fact]
    public void TooltipStaleTagged()
    {
        var s = new UsageSnapshot(42, null, null, null, Now, UsageSource.Probe);
        Assert.EndsWith("(stale)", Formatting.Tooltip(s, stale: true, authError: false, Now));
    }

    [Fact]
    public void TooltipAuthError()
        => Assert.Contains("not signed in", Formatting.Tooltip(null, false, authError: true, Now), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void TooltipNoDataYet()
        => Assert.Contains("waiting", Formatting.Tooltip(null, false, false, Now), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void AbsoluteLocalNullIsDash()
        => Assert.Equal("—", Formatting.AbsoluteLocal(null));
}
