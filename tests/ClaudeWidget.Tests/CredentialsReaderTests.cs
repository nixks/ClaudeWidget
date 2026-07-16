using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class CredentialsReaderTests
{
    private static string TempFile(string? content)
    {
        var path = Path.Combine(Path.GetTempPath(), "cw-" + Path.GetRandomFileName());
        if (content is not null) File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadsAccessToken()
    {
        var path = TempFile("""{"claudeAiOauth":{"accessToken":"tok-abc","refreshToken":"r"}}""");
        Assert.Equal("tok-abc", new CredentialsReader(path).ReadAccessToken());
    }

    [Fact]
    public void MissingFileReturnsNull()
        => Assert.Null(new CredentialsReader(TempFile(null)).ReadAccessToken());

    [Fact]
    public void MalformedJsonReturnsNull()
        => Assert.Null(new CredentialsReader(TempFile("{oops")).ReadAccessToken());

    [Fact]
    public void MissingTokenPropertyReturnsNull()
        => Assert.Null(new CredentialsReader(TempFile("""{"claudeAiOauth":{}}""")).ReadAccessToken());

    [Fact]
    public void EmptyTokenReturnsNull()
        => Assert.Null(new CredentialsReader(TempFile("""{"claudeAiOauth":{"accessToken":""}}""")).ReadAccessToken());

    [Fact]
    public void RereadPicksUpChangedToken()
    {
        var path = TempFile("""{"claudeAiOauth":{"accessToken":"tok-old"}}""");
        var reader = new CredentialsReader(path);
        Assert.Equal("tok-old", reader.ReadAccessToken());
        File.WriteAllText(path, """{"claudeAiOauth":{"accessToken":"tok-new"}}""");
        Assert.Equal("tok-new", reader.ReadAccessToken());
    }
}
