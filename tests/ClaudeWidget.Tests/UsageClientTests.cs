using System.Net;
using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class UsageClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Responder(request));
        }
    }

    private static CredentialsReader TempCreds(string token = "tok-123")
    {
        var path = Path.Combine(Path.GetTempPath(), "cw-" + Path.GetRandomFileName());
        var json = "{\"claudeAiOauth\":{\"accessToken\":\"" + token + "\"}}";
        File.WriteAllText(path, json);
        return new CredentialsReader(path);
    }

    private static HttpResponseMessage UsageOk() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"five_hour":{"utilization":42},"seven_day":{"utilization":31}}"""),
    };

    private static HttpResponseMessage ProbeOk(HttpStatusCode status = HttpStatusCode.OK)
    {
        var resp = new HttpResponseMessage(status) { Content = new StringContent("{}") };
        resp.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-5h-utilization", "0.42");
        resp.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-7d-utilization", "0.31");
        return resp;
    }

    [Fact]
    public async Task UsageEndpointSuccess()
    {
        var handler = new StubHandler { Responder = _ => UsageOk() };
        var client = new UsageClient(new HttpClient(handler), TempCreds());
        var result = await client.FetchAsync();
        Assert.Equal(FetchStatus.Ok, result.Status);
        Assert.Equal(UsageSource.UsageEndpoint, result.Snapshot!.Source);
        Assert.Equal(42, result.Snapshot.SessionPercent);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", req.RequestUri!.ToString());
        Assert.Equal("Bearer tok-123", req.Headers.GetValues("Authorization").Single());
        Assert.Equal("oauth-2025-04-20", req.Headers.GetValues("anthropic-beta").Single());
    }

    [Fact]
    public async Task FallsBackToProbeWhenUsageEndpointFails()
    {
        var handler = new StubHandler
        {
            Responder = req => req.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : ProbeOk(),
        };
        var result = await new UsageClient(new HttpClient(handler), TempCreds()).FetchAsync();
        Assert.Equal(FetchStatus.Ok, result.Status);
        Assert.Equal(UsageSource.Probe, result.Snapshot!.Source);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task ProbeHeadersParsedEvenOn429()
    {
        var handler = new StubHandler
        {
            Responder = req => req.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : ProbeOk(HttpStatusCode.TooManyRequests),
        };
        var result = await new UsageClient(new HttpClient(handler), TempCreds()).FetchAsync();
        Assert.Equal(FetchStatus.Ok, result.Status);
        Assert.Equal(UsageSource.Probe, result.Snapshot!.Source);
    }

    [Fact]
    public async Task RetriesOnceAfter401()
    {
        var calls = 0;
        var handler = new StubHandler
        {
            Responder = _ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : UsageOk(),
        };
        var result = await new UsageClient(new HttpClient(handler), TempCreds()).FetchAsync();
        Assert.Equal(FetchStatus.Ok, result.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Persistent401IsAuthError()
    {
        var handler = new StubHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) };
        var result = await new UsageClient(new HttpClient(handler), TempCreds()).FetchAsync();
        Assert.Equal(FetchStatus.AuthError, result.Status);
        Assert.Equal(2, handler.Requests.Count); // two GETs, no probe attempted
    }

    [Fact]
    public async Task BothSourcesFailingIsFailure()
    {
        var handler = new StubHandler(); // 500 for everything, no headers
        var result = await new UsageClient(new HttpClient(handler), TempCreds()).FetchAsync();
        Assert.Equal(FetchStatus.Failure, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task MissingCredentialsIsAuthErrorWithoutHttpCalls()
    {
        var handler = new StubHandler();
        var missing = new CredentialsReader(Path.Combine(Path.GetTempPath(), "cw-" + Path.GetRandomFileName()));
        var result = await new UsageClient(new HttpClient(handler), missing).FetchAsync();
        Assert.Equal(FetchStatus.AuthError, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NetworkExceptionIsFailure()
    {
        var handler = new StubHandler { Responder = _ => throw new HttpRequestException("boom") };
        var result = await new UsageClient(new HttpClient(handler), TempCreds()).FetchAsync();
        Assert.Equal(FetchStatus.Failure, result.Status);
    }
}
