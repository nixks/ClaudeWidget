using System.Text.Json;

namespace ClaudeWidget.Core;

public sealed class CredentialsReader
{
    private readonly string _path;

    public CredentialsReader(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", ".credentials.json");

    private const int MaxAttempts = 3;
    private const int RetryDelayMs = 25;

    public string? ReadAccessToken()
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                if (!File.Exists(_path)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) &&
                    oauth.ValueKind == JsonValueKind.Object &&
                    oauth.TryGetProperty("accessToken", out var token) &&
                    token.ValueKind == JsonValueKind.String)
                {
                    var value = token.GetString();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
                return null;
            }
            // The credentials file can be briefly locked or mid-write while Claude Code
            // refreshes the token. Retry a couple of times before giving up so that doesn't
            // get reported to the user as "not signed in".
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                if (attempt == MaxAttempts) return null;
                Thread.Sleep(RetryDelayMs);
            }
        }
        return null;
    }
}
