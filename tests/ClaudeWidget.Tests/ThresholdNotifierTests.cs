using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class ThresholdNotifierTests
{
    private static readonly DateTimeOffset Reset1 = new(2026, 7, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset2 = new(2026, 7, 16, 23, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snap(double pct, DateTimeOffset? resetsAt = null)
        => new(pct, resetsAt ?? Reset1, null, null, DateTimeOffset.UtcNow, UsageSource.UsageEndpoint);

    [Fact]
    public void FiresWarnOnceOnCrossing()
    {
        var n = new ThresholdNotifier(80, 95);
        Assert.Null(n.Update(Snap(50)));
        var msg = n.Update(Snap(85));
        Assert.NotNull(msg);
        Assert.DoesNotContain("nearly rate-limited", msg);
        Assert.Null(n.Update(Snap(86)));
        Assert.Null(n.Update(Snap(90)));
    }

    [Fact]
    public void FiresCriticalAfterWarn()
    {
        var n = new ThresholdNotifier(80, 95);
        n.Update(Snap(85));
        var msg = n.Update(Snap(96));
        Assert.NotNull(msg);
        Assert.Contains("nearly rate-limited", msg);
        Assert.Null(n.Update(Snap(97)));
    }

    [Fact]
    public void JumpPastBothFiresOnlyCritical()
    {
        var n = new ThresholdNotifier(80, 95);
        var msg = n.Update(Snap(96));
        Assert.NotNull(msg);
        Assert.Contains("nearly rate-limited", msg);
        Assert.Null(n.Update(Snap(97)));
    }

    [Fact]
    public void RearmsWhenSessionWindowResets()
    {
        var n = new ThresholdNotifier(80, 95);
        Assert.NotNull(n.Update(Snap(85, Reset1)));
        Assert.Null(n.Update(Snap(5, Reset2)));
        Assert.NotNull(n.Update(Snap(85, Reset2)));
    }

    [Fact]
    public void RearmsOnSharpDropWithoutResetChange()
    {
        var n = new ThresholdNotifier(80, 95);
        Assert.NotNull(n.Update(Snap(85)));
        Assert.Null(n.Update(Snap(10)));
        Assert.NotNull(n.Update(Snap(85)));
    }

    [Fact]
    public void FirstUpdateAboveThresholdFires()
    {
        var n = new ThresholdNotifier(80, 95);
        Assert.NotNull(n.Update(Snap(85)));
    }
}
