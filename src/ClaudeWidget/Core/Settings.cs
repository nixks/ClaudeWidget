using System.Text.Json;

namespace ClaudeWidget.Core;

public sealed class Settings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public int PollIntervalSeconds { get; set; } = 60;
    public double WarnThreshold { get; set; } = 80;
    public double CriticalThreshold { get; set; } = 95;
    public bool StartWithWindows { get; set; }
    public int? FlyoutX { get; set; }
    public int? FlyoutY { get; set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeWidget", "settings.json");

    private const int MinPollIntervalSeconds = 5;
    private const int MaxPollIntervalSeconds = 3600;

    public static Settings Load(string path)
    {
        try
        {
            Settings result;
            if (!File.Exists(path))
            {
                result = new Settings();
            }
            else
            {
                result = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? new Settings();
            }

            result.PollIntervalSeconds = Math.Clamp(result.PollIntervalSeconds, MinPollIntervalSeconds, MaxPollIntervalSeconds);
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
