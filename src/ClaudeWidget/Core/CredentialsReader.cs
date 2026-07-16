using System.Text.Json;

namespace ClaudeWidget.Core;

public sealed class CredentialsReader
{
    private readonly string _path;

    public CredentialsReader(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", ".credentials.json");

    public string? ReadAccessToken()
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
