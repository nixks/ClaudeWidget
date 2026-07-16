using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class ThresholdNotifierTests
{
    private static readonly DateTimeOffset Reset1 = new(2026, 7, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset2 = new(2026, 7, 16, 23, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snap(double pct, DateTimeOffset? resetsAt = null)
        => new(pct, resetsAt ?? Reset1, null, null, DateTimeOffset.UtcNow, UsageSource.UsageEndpoint);

    private static UsageSnapshot SnapWithNoReset(double pct)
        => new(pct, null, null, null, DateTimeOffset.UtcNow, UsageSource.Probe);

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

    [Fact]
    public void NullResetFromProbeFallbackDoesNotReArmDuplicateToast()
    {
        var n = new ThresholdNotifier(80, 95);
        // Fires warn on the usage endpoint with a known reset time.
        Assert.NotNull(n.Update(Snap(85, Reset1)));
        // A poll falls back to the probe, which lacks the reset header (null).
        // This must NOT be treated as a window change and must NOT re-fire.
        Assert.Null(n.Update(SnapWithNoReset(85)));
        // Flipping back to the original (non-null) reset at the same percent
        // must also NOT re-fire — it's the same window, not a genuine reset.
        Assert.Null(n.Update(Snap(85, Reset1)));
        // A genuinely different non-null reset still re-arms as before.
        Assert.NotNull(n.Update(Snap(85, Reset2)));
    }
}
