using System.Net;
using System.Net.Http.Json;

namespace ClaudeWidget.Core;

public enum FetchStatus
{
    Ok,
    AuthError,
    Failure,
}

public sealed record FetchResult(FetchStatus Status, UsageSnapshot? Snapshot);

public sealed class UsageClient
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string OAuthBeta = "oauth-2025-04-20";

    private readonly HttpClient _http;
    private readonly CredentialsReader _credentials;

    public UsageClient(HttpClient http, CredentialsReader credentials)
    {
        _http = http;
        _credentials = credentials;
    }

    public async Task<FetchResult> FetchAsync(CancellationToken ct = default)
    {
        var token = _credentials.ReadAccessToken();
        if (token is null) return new FetchResult(FetchStatus.AuthError, null);

        try
        {
            var (unauthorized, snapshot) = await TryUsageEndpointAsync(token, ct);
            if (unauthorized)
            {
                token = _credentials.ReadAccessToken();
                if (token is null) return new FetchResult(FetchStatus.AuthError, null);
                (unauthorized, snapshot) = await TryUsageEndpointAsync(token, ct);
                if (unauthorized) return new FetchResult(FetchStatus.AuthError, null);
            }
            if (snapshot is not null) return new FetchResult(FetchStatus.Ok, snapshot);

            var (probeUnauthorized, probeSnapshot) = await TryProbeAsync(token, ct);
            if (probeUnauthorized) return new FetchResult(FetchStatus.AuthError, null);
            return probeSnapshot is not null
                ? new FetchResult(FetchStatus.Ok, probeSnapshot)
                : new FetchResult(FetchStatus.Failure, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new FetchResult(FetchStatus.Failure, null);
        }
    }

    private async Task<(bool Unauthorized, UsageSnapshot? Snapshot)> TryUsageEndpointAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        AddAuthHeaders(request, token);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return (true, null);
        if (!response.IsSuccessStatusCode) return (false, null);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (false, UsageParser.ParseUsageJson(body, DateTimeOffset.UtcNow));
    }

    private async Task<(bool Unauthorized, UsageSnapshot? Snapshot)> TryProbeAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        AddAuthHeaders(request, token);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 1,
            system = "You are Claude Code, Anthropic's official CLI for Claude.",
            messages = new[] { new { role = "user", content = "." } },
        });
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return (true, null);
        string? Header(string name)
            => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
        return (false, UsageParser.ParseProbeHeaders(Header, DateTimeOffset.UtcNow));
    }

    private static void AddAuthHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBeta);
    }
}
