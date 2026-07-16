using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class SettingsTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), "cw-settings-" + Path.GetRandomFileName(), "settings.json");

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var s = Settings.Load(TempPath());
        Assert.Equal(60, s.PollIntervalSeconds);
        Assert.Equal(80, s.WarnThreshold);
        Assert.Equal(95, s.CriticalThreshold);
        Assert.False(s.StartWithWindows);
    }

    [Fact]
    public void CorruptFileGivesDefaults()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json");
        Assert.Equal(60, Settings.Load(path).PollIntervalSeconds);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var path = TempPath();
        var s = new Settings { PollIntervalSeconds = 120, WarnThreshold = 70, CriticalThreshold = 90, StartWithWindows = true };
        s.Save(path);
        var loaded = Settings.Load(path);
        Assert.Equal(120, loaded.PollIntervalSeconds);
        Assert.Equal(70, loaded.WarnThreshold);
        Assert.Equal(90, loaded.CriticalThreshold);
        Assert.True(loaded.StartWithWindows);
    }

    [Fact]
    public void SavedFileUsesCamelCase()
    {
        var path = TempPath();
        new Settings().Save(path);
        Assert.Contains("\"pollIntervalSeconds\"", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(999999)]
    public void OutOfRangePollIntervalIsClamped(int rawValue)
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{{\"pollIntervalSeconds\": {rawValue}}}");

        var loaded = Settings.Load(path);

        Assert.InRange(loaded.PollIntervalSeconds, 5, 3600);
        Assert.NotEqual(rawValue, loaded.PollIntervalSeconds);
    }
}
