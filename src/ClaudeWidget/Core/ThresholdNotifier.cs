namespace ClaudeWidget.Core;

public sealed class ThresholdNotifier
{
    private const double SharpDropPoints = 10;

    private readonly double _warn;
    private readonly double _critical;
    private bool _warnFired;
    private bool _criticalFired;
    private double _lastPercent = -1;
    private DateTimeOffset? _lastResetAt;
    private bool _hasSeenSnapshot;

    public ThresholdNotifier(double warnThreshold, double criticalThreshold)
    {
        _warn = warnThreshold;
        _critical = criticalThreshold;
    }

    public string? Update(UsageSnapshot snapshot)
    {
        var windowChanged = _hasSeenSnapshot
            && _lastResetAt is { } prevReset
            && snapshot.SessionResetsAtUtc is { } currReset
            && prevReset != currReset;
        var sharpDrop = _hasSeenSnapshot && snapshot.SessionPercent < _lastPercent - SharpDropPoints;
        if (windowChanged || sharpDrop)
        {
            _warnFired = false;
            _criticalFired = false;
        }
        _hasSeenSnapshot = true;
        if (snapshot.SessionResetsAtUtc is not null)
        {
            // Retain the last known non-null reset timestamp so a temporary gap
            // (e.g. probe fallback missing the reset header) doesn't erase our
            // ability to detect a genuine window change once a reset reappears.
            _lastResetAt = snapshot.SessionResetsAtUtc;
        }
        _lastPercent = snapshot.SessionPercent;

        var pct = Math.Round(snapshot.SessionPercent);
        if (!_criticalFired && snapshot.SessionPercent >= _critical)
        {
            _criticalFired = true;
            _warnFired = true;
            return $"Claude session usage at {pct:0}% — nearly rate-limited";
        }
        if (!_warnFired && snapshot.SessionPercent >= _warn)
        {
            _warnFired = true;
            return $"Claude session usage at {pct:0}%";
        }
        return null;
    }
}
