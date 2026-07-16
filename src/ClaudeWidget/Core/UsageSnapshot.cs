namespace ClaudeWidget.Core;

public enum UsageSource
{
    UsageEndpoint,
    Probe,
}

public sealed record UsageSnapshot(
    double SessionPercent,
    DateTimeOffset? SessionResetsAtUtc,
    double? WeeklyPercent,
    DateTimeOffset? WeeklyResetsAtUtc,
    DateTimeOffset FetchedAtUtc,
    UsageSource Source);
