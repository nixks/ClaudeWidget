# ClaudeWidget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows tray widget (.NET 8 WinForms) showing Claude session %, time until reset, and weekly usage, with a click flyout, color-coded live icon, threshold toasts, autostart, and configurable polling.

**Architecture:** Single-process WinForms tray app. Pure, unit-tested Core classes (parsers, client, notifier, settings, autostart, formatting) with a thin UI layer (runtime-drawn tray icon, borderless flyout form, ApplicationContext wiring). Data comes from the OAuth usage endpoint with a probe-request fallback, authenticated by the Claude Code credentials file.

**Tech Stack:** .NET 8 (`net8.0-windows`), WinForms, System.Text.Json (in-box), xUnit. No external runtime NuGet packages in the app project.

**Spec:** `docs/superpowers/specs/2026-07-16-claude-usage-tray-widget-design.md`

## Global Constraints

- Target framework `net8.0-windows`, `<UseWindowsForms>true</UseWindowsForms>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` in both projects.
- App project has **zero** PackageReference entries; test project uses xUnit + Microsoft.NET.Test.Sdk only.
- Namespaces: `ClaudeWidget.Core` for logic, `ClaudeWidget.UI` for icon/flyout, `ClaudeWidget` for Program/TrayAppContext.
- All timestamps `DateTimeOffset` in UTC internally; convert to local time only at display.
- Utilization normalization rule (spec): raw values `<= 1.0` are fractions → multiply by 100; otherwise already percent.
- Icon/bar color thresholds: green `< 70`, amber `70–89.99`, red `>= 90`; gray when stale/auth-error/no-data.
- Stale = 3 consecutive failed polls. Auth-error = missing credentials or persistent 401.
- Settings file: `%APPDATA%\ClaudeWidget\settings.json`, camelCase JSON, defaults `{ pollIntervalSeconds: 60, warnThreshold: 80, criticalThreshold: 95, startWithWindows: false }`.
- Every commit message ends with the trailer line: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Run all tests from repo root with: `dotnet test` — expect PASS before every commit.
- If `dotnet --version` is not ≥ 8.0, install with `winget install Microsoft.DotNet.SDK.8` before Task 1.

---

### Task 1: Solution scaffold

**Files:**
- Create: `.gitignore`, `ClaudeWidget.sln`, `src/ClaudeWidget/ClaudeWidget.csproj`, `src/ClaudeWidget/Program.cs`, `tests/ClaudeWidget.Tests/ClaudeWidget.Tests.csproj`, `tests/ClaudeWidget.Tests/SmokeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: buildable solution; later tasks add files under `src/ClaudeWidget/Core`, `src/ClaudeWidget/UI`, and tests under `tests/ClaudeWidget.Tests`.

- [ ] **Step 1: Create .gitignore**

`.gitignore`:
```
bin/
obj/
publish/
*.user
```

- [ ] **Step 2: Create app project**

`src/ClaudeWidget/ClaudeWidget.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>ClaudeWidget</AssemblyName>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
  </PropertyGroup>
</Project>
```

`src/ClaudeWidget/Program.cs` (placeholder; replaced in Task 12):
```csharp
namespace ClaudeWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
    }
}
```

- [ ] **Step 3: Create test project**

`tests/ClaudeWidget.Tests/ClaudeWidget.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ClaudeWidget\ClaudeWidget.csproj" />
  </ItemGroup>
</Project>
```

`tests/ClaudeWidget.Tests/SmokeTests.cs`:
```csharp
namespace ClaudeWidget.Tests;

public class SmokeTests
{
    [Fact]
    public void TestFrameworkRuns() => Assert.True(true);
}
```

- [ ] **Step 4: Create solution and add projects**

Run:
```powershell
dotnet new sln -n ClaudeWidget && dotnet sln add src/ClaudeWidget/ClaudeWidget.csproj tests/ClaudeWidget.Tests/ClaudeWidget.Tests.csproj
```

- [ ] **Step 5: Verify build and tests**

Run: `dotnet test`
Expected: build succeeds, 1 test passes.

- [ ] **Step 6: Commit**

```powershell
git add -A && git commit -m "chore: scaffold .NET 8 WinForms solution with xUnit test project"
```

---

### Task 2: UsageSnapshot model + usage-endpoint JSON parser

**Files:**
- Create: `src/ClaudeWidget/Core/UsageSnapshot.cs`, `src/ClaudeWidget/Core/UsageParser.cs`
- Test: `tests/ClaudeWidget.Tests/UsageParserTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum UsageSource { UsageEndpoint, Probe }`
  - `record UsageSnapshot(double SessionPercent, DateTimeOffset? SessionResetsAtUtc, double? WeeklyPercent, DateTimeOffset? WeeklyResetsAtUtc, DateTimeOffset FetchedAtUtc, UsageSource Source)`
  - `static UsageSnapshot? UsageParser.ParseUsageJson(string json, DateTimeOffset nowUtc)`
  - `static double UsageParser.NormalizePercent(double raw)`

- [ ] **Step 1: Write the failing tests**

`tests/ClaudeWidget.Tests/UsageParserTests.cs`:
```csharp
using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class UsageParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParsesFullResponseWithPercentScale()
    {
        var json = """
            {"five_hour":{"utilization":42,"resets_at":"2026-07-16T18:00:00Z"},
             "seven_day":{"utilization":31,"resets_at":"2026-07-21T08:00:00Z"}}
            """;
        var s = UsageParser.ParseUsageJson(json, Now);
        Assert.NotNull(s);
        Assert.Equal(42, s!.SessionPercent);
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 18, 0, 0, TimeSpan.Zero), s.SessionResetsAtUtc);
        Assert.Equal(31, s.WeeklyPercent);
        Assert.Equal(new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.Zero), s.WeeklyResetsAtUtc);
        Assert.Equal(Now, s.FetchedAtUtc);
        Assert.Equal(UsageSource.UsageEndpoint, s.Source);
    }

    [Fact]
    public void NormalizesFractionScale()
    {
        var json = """{"five_hour":{"utilization":0.42},"seven_day":{"utilization":0.31}}""";
        var s = UsageParser.ParseUsageJson(json, Now);
        Assert.Equal(42, s!.SessionPercent, precision: 5);
        Assert.Equal(31, s.WeeklyPercent!.Value, precision: 5);
    }

    [Fact]
    public void ParsesUnixNumberResetTimestamp()
    {
        var json = """{"five_hour":{"utilization":10,"resets_at":1784224800}}""";
        var s = UsageParser.ParseUsageJson(json, Now);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784224800), s!.SessionResetsAtUtc);
    }

    [Fact]
    public void MissingSevenDayGivesNullWeekly()
    {
        var json = """{"five_hour":{"utilization":10}}""";
        var s = UsageParser.ParseUsageJson(json, Now);
        Assert.NotNull(s);
        Assert.Null(s!.WeeklyPercent);
        Assert.Null(s.WeeklyResetsAtUtc);
    }

    [Fact]
    public void MissingFiveHourReturnsNull()
        => Assert.Null(UsageParser.ParseUsageJson("""{"seven_day":{"utilization":10}}""", Now));

    [Fact]
    public void GarbageReturnsNull()
    {
        Assert.Null(UsageParser.ParseUsageJson("not json at all", Now));
        Assert.Null(UsageParser.ParseUsageJson("""{"five_hour":{"utilization":"high"}}""", Now));
    }

    [Theory]
    [InlineData(0.5, 50)]
    [InlineData(1.0, 100)]
    [InlineData(42, 42)]
    [InlineData(0, 0)]
    public void NormalizePercentRule(double raw, double expected)
        => Assert.Equal(expected, UsageParser.NormalizePercent(raw));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `UsageParser`/`UsageSnapshot` do not exist.

- [ ] **Step 3: Implement model and parser**

`src/ClaudeWidget/Core/UsageSnapshot.cs`:
```csharp
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
```

`src/ClaudeWidget/Core/UsageParser.cs`:
```csharp
using System.Globalization;
using System.Text.Json;

namespace ClaudeWidget.Core;

public static class UsageParser
{
    public static UsageSnapshot? ParseUsageJson(string json, DateTimeOffset nowUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!TryGetWindow(root, "five_hour", out var sessionPct, out var sessionReset)) return null;
            double? weeklyPct = null;
            DateTimeOffset? weeklyReset = null;
            if (TryGetWindow(root, "seven_day", out var wp, out var wr))
            {
                weeklyPct = wp;
                weeklyReset = wr;
            }
            return new UsageSnapshot(sessionPct, sessionReset, weeklyPct, weeklyReset, nowUtc, UsageSource.UsageEndpoint);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static double NormalizePercent(double raw) => raw <= 1.0 ? raw * 100.0 : raw;

    private static bool TryGetWindow(JsonElement root, string name, out double percent, out DateTimeOffset? resetsAt)
    {
        percent = 0;
        resetsAt = null;
        if (!root.TryGetProperty(name, out var win) || win.ValueKind != JsonValueKind.Object) return false;
        if (!win.TryGetProperty("utilization", out var util) || util.ValueKind != JsonValueKind.Number) return false;
        percent = NormalizePercent(util.GetDouble());
        if (win.TryGetProperty("resets_at", out var reset))
        {
            if (reset.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dto))
            {
                resetsAt = dto;
            }
            else if (reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var unix))
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(unix);
            }
        }
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: usage snapshot model and usage-endpoint JSON parser"
```

---

### Task 3: Probe rate-limit header parser

**Files:**
- Modify: `src/ClaudeWidget/Core/UsageParser.cs` (add method)
- Test: `tests/ClaudeWidget.Tests/ProbeHeaderTests.cs`

**Interfaces:**
- Consumes: `UsageSnapshot`, `UsageParser.NormalizePercent` (Task 2).
- Produces: `static UsageSnapshot? UsageParser.ParseProbeHeaders(Func<string, string?> header, DateTimeOffset nowUtc)` — `header` returns the value of a named response header or null.

- [ ] **Step 1: Write the failing tests**

`tests/ClaudeWidget.Tests/ProbeHeaderTests.cs`:
```csharp
using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class ProbeHeaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private static Func<string, string?> Headers(Dictionary<string, string> d)
        => name => d.TryGetValue(name, out var v) ? v : null;

    [Fact]
    public void ParsesAllFourHeaders()
    {
        var h = Headers(new()
        {
            ["anthropic-ratelimit-unified-5h-utilization"] = "0.42",
            ["anthropic-ratelimit-unified-5h-reset"] = "1784224800",
            ["anthropic-ratelimit-unified-7d-utilization"] = "0.31",
            ["anthropic-ratelimit-unified-7d-reset"] = "1784656800",
        });
        var s = UsageParser.ParseProbeHeaders(h, Now);
        Assert.NotNull(s);
        Assert.Equal(42, s!.SessionPercent, precision: 5);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784224800), s.SessionResetsAtUtc);
        Assert.Equal(31, s.WeeklyPercent!.Value, precision: 5);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784656800), s.WeeklyResetsAtUtc);
        Assert.Equal(UsageSource.Probe, s.Source);
    }

    [Fact]
    public void ParsesIsoResetTimestamp()
    {
        var h = Headers(new()
        {
            ["anthropic-ratelimit-unified-5h-utilization"] = "0.1",
            ["anthropic-ratelimit-unified-5h-reset"] = "2026-07-16T18:00:00Z",
        });
        var s = UsageParser.ParseProbeHeaders(h, Now);
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 18, 0, 0, TimeSpan.Zero), s!.SessionResetsAtUtc);
    }

    [Fact]
    public void MissingSessionUtilizationReturnsNull()
        => Assert.Null(UsageParser.ParseProbeHeaders(Headers(new()), Now));

    [Fact]
    public void MissingWeeklyHeadersGiveNullWeekly()
    {
        var h = Headers(new() { ["anthropic-ratelimit-unified-5h-utilization"] = "0.5" });
        var s = UsageParser.ParseProbeHeaders(h, Now);
        Assert.NotNull(s);
        Assert.Null(s!.WeeklyPercent);
        Assert.Null(s.SessionResetsAtUtc);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `ParseProbeHeaders` does not exist.

- [ ] **Step 3: Implement**

Add to `src/ClaudeWidget/Core/UsageParser.cs` inside the `UsageParser` class:
```csharp
    public static UsageSnapshot? ParseProbeHeaders(Func<string, string?> header, DateTimeOffset nowUtc)
    {
        var sessionUtil = ParseInvariantDouble(header("anthropic-ratelimit-unified-5h-utilization"));
        if (sessionUtil is null) return null;
        var sessionReset = ParseResetValue(header("anthropic-ratelimit-unified-5h-reset"));
        var weeklyUtil = ParseInvariantDouble(header("anthropic-ratelimit-unified-7d-utilization"));
        var weeklyReset = ParseResetValue(header("anthropic-ratelimit-unified-7d-reset"));
        return new UsageSnapshot(
            NormalizePercent(sessionUtil.Value),
            sessionReset,
            weeklyUtil is null ? null : NormalizePercent(weeklyUtil.Value),
            weeklyReset,
            nowUtc,
            UsageSource.Probe);
    }

    private static double? ParseInvariantDouble(string? s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseResetValue(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
            return DateTimeOffset.FromUnixTimeSeconds(unix);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: probe rate-limit header parser"
```

---

### Task 4: CredentialsReader

**Files:**
- Create: `src/ClaudeWidget/Core/CredentialsReader.cs`
- Test: `tests/ClaudeWidget.Tests/CredentialsReaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `class CredentialsReader` with ctor `CredentialsReader(string? path = null)` (default path `%USERPROFILE%\.claude\.credentials.json`) and `string? ReadAccessToken()` — reads the file fresh on every call; null on any problem.

- [ ] **Step 1: Write the failing tests**

`tests/ClaudeWidget.Tests/CredentialsReaderTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `CredentialsReader` does not exist.

- [ ] **Step 3: Implement**

`src/ClaudeWidget/Core/CredentialsReader.cs`:
```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: read Claude Code OAuth token from credentials file"
```

---

### Task 5: Settings (load/save)

**Files:**
- Create: `src/ClaudeWidget/Core/Settings.cs`
- Test: `tests/ClaudeWidget.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `class Settings` with properties `int PollIntervalSeconds` (60), `double WarnThreshold` (80), `double CriticalThreshold` (95), `bool StartWithWindows` (false); `static Settings Load(string path)` (defaults on any problem), `void Save(string path)` (creates directory), `static string DefaultPath`.

- [ ] **Step 1: Write the failing tests**

`tests/ClaudeWidget.Tests/SettingsTests.cs`:
```csharp
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `Settings` does not exist.

- [ ] **Step 3: Implement**

`src/ClaudeWidget/Core/Settings.cs`:
```csharp
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

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeWidget", "settings.json");

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? new Settings();
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: settings persistence with defaults"
```

---

### Task 6: UsageClient (usage endpoint + probe fallback + 401 handling)

**Files:**
- Create: `src/ClaudeWidget/Core/UsageClient.cs`
- Test: `tests/ClaudeWidget.Tests/UsageClientTests.cs`

**Interfaces:**
- Consumes: `CredentialsReader.ReadAccessToken()` (Task 4), `UsageParser.ParseUsageJson` / `ParseProbeHeaders` (Tasks 2–3).
- Produces:
  - `enum FetchStatus { Ok, AuthError, Failure }`
  - `record FetchResult(FetchStatus Status, UsageSnapshot? Snapshot)`
  - `class UsageClient` with ctor `UsageClient(HttpClient http, CredentialsReader credentials)` and `Task<FetchResult> FetchAsync(CancellationToken ct = default)`

Behavior contract:
1. No token → `AuthError` without any HTTP call.
2. GET `https://api.anthropic.com/api/oauth/usage` with `Authorization: Bearer <token>` and `anthropic-beta: oauth-2025-04-20`. On 401 → re-read token, retry once; still 401 → `AuthError`.
3. 2xx + parseable body → `Ok` (source UsageEndpoint).
4. Otherwise fall back to probe: POST `https://api.anthropic.com/v1/messages` (headers above plus `anthropic-version: 2023-06-01`; body: model `claude-haiku-4-5-20251001`, `max_tokens: 1`, system `"You are Claude Code, Anthropic's official CLI for Claude."`, one user message `"."`). 401 → `AuthError`. Otherwise parse rate-limit headers regardless of status code (a 429 still carries them) → `Ok` if parseable, else `Failure`.
5. Network exceptions → `Failure`.

- [ ] **Step 1: Write the failing tests**

`tests/ClaudeWidget.Tests/UsageClientTests.cs`:
```csharp
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
        File.WriteAllText(path, $$"""{"claudeAiOauth":{"accessToken":"{{token}}"}}""");
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `UsageClient`/`FetchResult`/`FetchStatus` do not exist.

- [ ] **Step 3: Implement**

`src/ClaudeWidget/Core/UsageClient.cs`:
```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: usage client with endpoint-first fetch, probe fallback, and 401 retry"
```

---

### Task 7: ThresholdNotifier

**Files:**
- Create: `src/ClaudeWidget/Core/ThresholdNotifier.cs`
- Test: `tests/ClaudeWidget.Tests/ThresholdNotifierTests.cs`

**Interfaces:**
- Consumes: `UsageSnapshot` (Task 2).
- Produces: `class ThresholdNotifier` with ctor `ThresholdNotifier(double warnThreshold, double criticalThreshold)` and `string? Update(UsageSnapshot snapshot)` — returns a notification message on an upward threshold crossing, else null. Stateful: fires each threshold once per session window; re-arms when `SessionResetsAtUtc` changes or usage drops by more than 10 points.

- [ ] **Step 1: Write the failing tests**

`tests/ClaudeWidget.Tests/ThresholdNotifierTests.cs`:
```csharp
using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class ThresholdNotifierTests
{
    private static readonly DateTimeOffset Reset1 = new(2026, 7, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset2 = new(2026, 7, 16, 23, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snap(double pct, DateTimeOffset? resetsAt = null)
        => new(pct, resetsAt ?? Reset1, null, null, DateTimeOffset.UtcNow, UsageSource.UsageEndpoint);

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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `ThresholdNotifier` does not exist.

- [ ] **Step 3: Implement**

`src/ClaudeWidget/Core/ThresholdNotifier.cs`:
```csharp
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
        var windowChanged = _hasSeenSnapshot && _lastResetAt != snapshot.SessionResetsAtUtc;
        var sharpDrop = _hasSeenSnapshot && snapshot.SessionPercent < _lastPercent - SharpDropPoints;
        if (windowChanged || sharpDrop)
        {
            _warnFired = false;
            _criticalFired = false;
        }
        _hasSeenSnapshot = true;
        _lastResetAt = snapshot.SessionResetsAtUtc;
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: edge-triggered threshold notifier with re-arm"
```

---

### Task 8: Formatting helpers (countdown, tooltip)

**Files:**
- Create: `src/ClaudeWidget/Core/Formatting.cs`
- Test: `tests/ClaudeWidget.Tests/FormattingTests.cs`

**Interfaces:**
- Consumes: `UsageSnapshot` (Task 2).
- Produces:
  - `static string Formatting.Countdown(DateTimeOffset? resetsAt, DateTimeOffset now)` → `"2h 13m"`, `"45m"`, `"now"`, `"—"`
  - `static string Formatting.AbsoluteLocal(DateTimeOffset? resetsAt)` → `"Tue 21 Jul, 08:00"` (local time) or `"—"`
  - `static string Formatting.Tooltip(UsageSnapshot? s, bool stale, bool authError, DateTimeOffset now)`

- [ ] **Step 1: Write the failing tests**

`tests/ClaudeWidget.Tests/FormattingTests.cs`:
```csharp
using ClaudeWidget.Core;

namespace ClaudeWidget.Tests;

public class FormattingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CountdownHoursAndMinutes()
        => Assert.Equal("2h 13m", Formatting.Countdown(Now.AddHours(2).AddMinutes(13), Now));

    [Fact]
    public void CountdownMinutesOnly()
        => Assert.Equal("45m", Formatting.Countdown(Now.AddMinutes(45), Now));

    [Fact]
    public void CountdownPastIsNow()
        => Assert.Equal("now", Formatting.Countdown(Now.AddMinutes(-1), Now));

    [Fact]
    public void CountdownNullIsDash()
        => Assert.Equal("—", Formatting.Countdown(null, Now));

    [Fact]
    public void TooltipNormal()
    {
        var s = new UsageSnapshot(42, Now.AddHours(2).AddMinutes(13), 31, null, Now, UsageSource.UsageEndpoint);
        var tip = Formatting.Tooltip(s, stale: false, authError: false, Now);
        Assert.Equal("Claude — session 42%, resets in 2h 13m · wk 31%", tip);
        Assert.True(tip.Length <= 127);
    }

    [Fact]
    public void TooltipStaleTagged()
    {
        var s = new UsageSnapshot(42, null, null, null, Now, UsageSource.Probe);
        Assert.EndsWith("(stale)", Formatting.Tooltip(s, stale: true, authError: false, Now));
    }

    [Fact]
    public void TooltipAuthError()
        => Assert.Contains("not signed in", Formatting.Tooltip(null, false, authError: true, Now), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void TooltipNoDataYet()
        => Assert.Contains("waiting", Formatting.Tooltip(null, false, false, Now), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void AbsoluteLocalNullIsDash()
        => Assert.Equal("—", Formatting.AbsoluteLocal(null));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `Formatting` does not exist.

- [ ] **Step 3: Implement**

`src/ClaudeWidget/Core/Formatting.cs`:
```csharp
using System.Globalization;

namespace ClaudeWidget.Core;

public static class Formatting
{
    public static string Countdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null) return "—";
        var delta = resetsAt.Value - now;
        if (delta <= TimeSpan.Zero) return "now";
        return delta.TotalHours >= 1
            ? $"{(int)delta.TotalHours}h {delta.Minutes:00}m"
            : $"{Math.Max(1, (int)Math.Ceiling(delta.TotalMinutes))}m";
    }

    public static string AbsoluteLocal(DateTimeOffset? resetsAt)
        => resetsAt?.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture) ?? "—";

    public static string Tooltip(UsageSnapshot? s, bool stale, bool authError, DateTimeOffset now)
    {
        if (authError) return "Claude — not signed in (credentials not found)";
        if (s is null) return "Claude — waiting for first update…";
        var weekly = s.WeeklyPercent is null ? "" : $" · wk {s.WeeklyPercent:0}%";
        var staleTag = stale ? " (stale)" : "";
        return $"Claude — session {s.SessionPercent:0}%, resets in {Countdown(s.SessionResetsAtUtc, now)}{weekly}{staleTag}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: countdown and tooltip formatting helpers"
```

---

### Task 9: Autostart (HKCU Run key)

**Files:**
- Create: `src/ClaudeWidget/Core/Autostart.cs`
- Test: `tests/ClaudeWidget.Tests/AutostartTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class Autostart` with `const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run"` and methods `bool IsEnabled(string keyPath = RunKeyPath)`, `void Enable(string exePath, string keyPath = RunKeyPath)`, `void Disable(string keyPath = RunKeyPath)` — value name is always `"ClaudeWidget"`, value is the quoted exe path.

- [ ] **Step 1: Write the failing test**

`tests/ClaudeWidget.Tests/AutostartTests.cs`:
```csharp
using ClaudeWidget.Core;
using Microsoft.Win32;

namespace ClaudeWidget.Tests;

public sealed class AutostartTests : IDisposable
{
    private const string TestRoot = @"Software\ClaudeWidgetTests";
    private const string TestKey = TestRoot + @"\Run";

    public void Dispose()
        => Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);

    [Fact]
    public void EnableIsEnabledDisableRoundTrip()
    {
        Assert.False(Autostart.IsEnabled(TestKey));
        Autostart.Enable(@"C:\fake dir\ClaudeWidget.exe", TestKey);
        Assert.True(Autostart.IsEnabled(TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.Equal("\"C:\\fake dir\\ClaudeWidget.exe\"", key!.GetValue("ClaudeWidget"));
        }
        Autostart.Disable(TestKey);
        Assert.False(Autostart.IsEnabled(TestKey));
    }

    [Fact]
    public void DisableWhenNotEnabledDoesNotThrow()
        => Autostart.Disable(TestKey);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `Autostart` does not exist.

- [ ] **Step 3: Implement**

`src/ClaudeWidget/Core/Autostart.cs`:
```csharp
using Microsoft.Win32;

namespace ClaudeWidget.Core;

public static class Autostart
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeWidget";

    public static bool IsEnabled(string keyPath = RunKeyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) is string;
    }

    public static void Enable(string exePath, string keyPath = RunKeyPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    public static void Disable(string keyPath = RunKeyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: autostart via HKCU Run key"
```

---

### Task 10: IconRenderer

**Files:**
- Create: `src/ClaudeWidget/UI/IconRenderer.cs`
- Test: `tests/ClaudeWidget.Tests/IconRendererTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static Color IconRenderer.ColorFor(double? sessionPercent, bool stale, bool authError)`
  - `static string IconRenderer.IconText(double? sessionPercent, bool authError)` → `"42"`, `"!!"` (≥ 99.5 rounds to 100), `"?"` (no data), `"!"` (auth error)
  - `static Icon IconRenderer.Render(string text, Color background)` — 32×32 icon, no GDI handle leaks

- [ ] **Step 1: Write the failing tests**

`tests/ClaudeWidget.Tests/IconRendererTests.cs`:
```csharp
using System.Drawing;
using ClaudeWidget.UI;

namespace ClaudeWidget.Tests;

public class IconRendererTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(69.9)]
    public void GreenBelow70(double pct)
        => Assert.Equal(IconRenderer.Green, IconRenderer.ColorFor(pct, false, false));

    [Theory]
    [InlineData(70)]
    [InlineData(89.9)]
    public void AmberFrom70(double pct)
        => Assert.Equal(IconRenderer.Amber, IconRenderer.ColorFor(pct, false, false));

    [Theory]
    [InlineData(90)]
    [InlineData(100)]
    public void RedFrom90(double pct)
        => Assert.Equal(IconRenderer.Red, IconRenderer.ColorFor(pct, false, false));

    [Fact]
    public void GrayWhenStaleAuthErrorOrNoData()
    {
        Assert.Equal(IconRenderer.Gray, IconRenderer.ColorFor(42, stale: true, authError: false));
        Assert.Equal(IconRenderer.Gray, IconRenderer.ColorFor(42, stale: false, authError: true));
        Assert.Equal(IconRenderer.Gray, IconRenderer.ColorFor(null, false, false));
    }

    [Theory]
    [InlineData(42.4, "42")]
    [InlineData(0, "0")]
    [InlineData(99.5, "!!")]
    [InlineData(100, "!!")]
    public void IconTextForPercent(double pct, string expected)
        => Assert.Equal(expected, IconRenderer.IconText(pct, authError: false));

    [Fact]
    public void IconTextSpecialStates()
    {
        Assert.Equal("!", IconRenderer.IconText(42, authError: true));
        Assert.Equal("?", IconRenderer.IconText(null, authError: false));
    }

    [Fact]
    public void RenderProducesIcon()
    {
        using var icon = IconRenderer.Render("42", IconRenderer.Green);
        Assert.Equal(32, icon.Width);
        Assert.Equal(32, icon.Height);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to compile — `IconRenderer` does not exist.

- [ ] **Step 3: Implement**

`src/ClaudeWidget/UI/IconRenderer.cs`:
```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeWidget.UI;

public static class IconRenderer
{
    public static readonly Color Green = Color.FromArgb(22, 163, 74);
    public static readonly Color Amber = Color.FromArgb(217, 119, 6);
    public static readonly Color Red = Color.FromArgb(220, 38, 38);
    public static readonly Color Gray = Color.FromArgb(110, 110, 110);

    public static Color ColorFor(double? sessionPercent, bool stale, bool authError)
    {
        if (authError || stale || sessionPercent is null) return Gray;
        return sessionPercent < 70 ? Green : sessionPercent < 90 ? Amber : Red;
    }

    public static string IconText(double? sessionPercent, bool authError)
    {
        if (authError) return "!";
        if (sessionPercent is null) return "?";
        var rounded = Math.Round(sessionPercent.Value);
        return rounded >= 100 ? "!!" : rounded.ToString("0");
    }

    public static Icon Render(string text, Color background)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var path = RoundedRect(new Rectangle(0, 0, size - 1, size - 1), 9))
            using (var brush = new SolidBrush(background))
            {
                g.FillPath(brush, path);
            }
            using var font = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 1, size, size), format);
        }
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: runtime-drawn tray icon with color thresholds"
```

---

### Task 11: FlyoutForm

**Files:**
- Create: `src/ClaudeWidget/UI/FlyoutForm.cs`

**Interfaces:**
- Consumes: `UsageSnapshot`, `UsageSource`, `Formatting` (Tasks 2, 8), `IconRenderer.ColorFor` (Task 10).
- Produces: `class FlyoutForm : Form` with `void UpdateFrom(UsageSnapshot? snapshot, bool stale, bool authError)` and `void ShowNearTray()`. Hides itself on deactivate. (UI-only task: verified by build + manual smoke test in Task 12.)

- [ ] **Step 1: Implement**

`src/ClaudeWidget/UI/FlyoutForm.cs`:
```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using ClaudeWidget.Core;
using Microsoft.Win32;

namespace ClaudeWidget.UI;

public sealed class FlyoutForm : Form
{
    private UsageSnapshot? _snapshot;
    private bool _stale;
    private bool _authError;

    private readonly Color _back;
    private readonly Color _fore;
    private readonly Color _dimFore;
    private readonly Color _barBack;
    private readonly Color _border;

    public FlyoutForm()
    {
        var dark = IsDarkMode();
        _back = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        _fore = dark ? Color.White : Color.FromArgb(27, 27, 27);
        _dimFore = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
        _barBack = dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(217, 217, 217);
        _border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(190, 190, 190);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(340, 160);
        BackColor = _back;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => false;

    public void UpdateFrom(UsageSnapshot? snapshot, bool stale, bool authError)
    {
        _snapshot = snapshot;
        _stale = stale;
        _authError = authError;
        Invalidate();
    }

    public void ShowNearTray()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 8, area.Bottom - Height - 8);
        Show();
        Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var titleFont = new Font("Segoe UI Semibold", 11f);
        using var font = new Font("Segoe UI", 9.5f);
        using var smallFont = new Font("Segoe UI", 8.5f);
        using var fore = new SolidBrush(_fore);
        using var dim = new SolidBrush(_dimFore);
        using var borderPen = new Pen(_border);

        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        g.DrawString("Claude Usage", titleFont, fore, 16, 12);

        if (_authError)
        {
            g.DrawString("Not signed in — Claude Code credentials not found.", font, fore, 16, 52);
            g.DrawString("Log in with Claude Code, then click Refresh now.", font, dim, 16, 74);
            return;
        }
        if (_snapshot is null)
        {
            g.DrawString("Waiting for first update…", font, fore, 16, 52);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        DrawRow(g, "Session", _snapshot.SessionPercent,
            $"resets in {Formatting.Countdown(_snapshot.SessionResetsAtUtc, now)}",
            48, font, smallFont, fore, dim);
        DrawRow(g, "Weekly", _snapshot.WeeklyPercent,
            $"resets {Formatting.AbsoluteLocal(_snapshot.WeeklyResetsAtUtc)}",
            92, font, smallFont, fore, dim);

        var source = _snapshot.Source == UsageSource.UsageEndpoint ? "usage endpoint" : "probe";
        var staleTag = _stale ? " · STALE" : "";
        g.DrawString(
            $"Updated {_snapshot.FetchedAtUtc.ToLocalTime():HH:mm:ss} · {source}{staleTag}",
            smallFont, dim, 16, 138);
    }

    private void DrawRow(Graphics g, string label, double? percent, string resetText, int y,
        Font font, Font smallFont, Brush fore, Brush dim)
    {
        g.DrawString(label, font, fore, 16, y);
        var bar = new Rectangle(84, y + 4, 150, 12);
        using (var back = new SolidBrush(_barBack))
        {
            g.FillRectangle(back, bar);
        }
        if (percent is not null)
        {
            var width = (int)(bar.Width * Math.Clamp(percent.Value, 0, 100) / 100.0);
            if (width > 0)
            {
                using var fill = new SolidBrush(IconRenderer.ColorFor(percent, stale: false, authError: false));
                g.FillRectangle(fill, new Rectangle(bar.X, bar.Y, width, bar.Height));
            }
            g.DrawString($"{percent:0}%", font, fore, 242, y);
        }
        else
        {
            g.DrawString("—", font, fore, 242, y);
        }
        g.DrawString(resetText, smallFont, dim, 84, y + 20);
    }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 2: Verify it builds and existing tests still pass**

Run: `dotnet test`
Expected: build succeeds, all existing tests PASS.

- [ ] **Step 3: Commit**

```powershell
git add -A && git commit -m "feat: flyout form with usage bars and theme awareness"
```

---

### Task 12: TrayAppContext + Program wiring (end-to-end)

**Files:**
- Create: `src/ClaudeWidget/TrayAppContext.cs`
- Modify: `src/ClaudeWidget/Program.cs` (replace placeholder)

**Interfaces:**
- Consumes: everything from Tasks 2–11 exactly as declared in their Produces blocks.
- Produces: runnable tray application.

- [ ] **Step 1: Implement TrayAppContext**

`src/ClaudeWidget/TrayAppContext.cs`:
```csharp
using ClaudeWidget.Core;
using ClaudeWidget.UI;

namespace ClaudeWidget;

public sealed class TrayAppContext : ApplicationContext
{
    private static readonly int[] PollChoices = [30, 60, 120, 300];

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly UsageClient _client;
    private readonly Settings _settings;
    private readonly ThresholdNotifier _notifier;
    private readonly FlyoutForm _flyout = new();
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _intervalMenu;

    private UsageSnapshot? _snapshot;
    private int _consecutiveFailures;
    private bool _authError;
    private bool _polling;

    public TrayAppContext()
    {
        _settings = Settings.Load(Settings.DefaultPath);
        _client = new UsageClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, new CredentialsReader());
        _notifier = new ThresholdNotifier(_settings.WarnThreshold, _settings.CriticalThreshold);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, async (_, _) => await PollAsync());

        _autostartItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled(),
        };
        _autostartItem.CheckedChanged += OnAutostartToggled;
        menu.Items.Add(_autostartItem);

        _intervalMenu = new ToolStripMenuItem("Poll interval");
        foreach (var seconds in PollChoices)
        {
            var item = new ToolStripMenuItem(seconds < 60 ? $"{seconds} s" : $"{seconds / 60} min")
            {
                Tag = seconds,
                Checked = seconds == _settings.PollIntervalSeconds,
            };
            item.Click += OnIntervalSelected;
            _intervalMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(_intervalMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleFlyout();
        };
        UpdateTray();

        _timer = new System.Windows.Forms.Timer { Interval = _settings.PollIntervalSeconds * 1000 };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    private bool IsStale => _consecutiveFailures >= 3;

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var result = await _client.FetchAsync();
            switch (result.Status)
            {
                case FetchStatus.Ok:
                    _snapshot = result.Snapshot;
                    _consecutiveFailures = 0;
                    _authError = false;
                    var message = _notifier.Update(_snapshot!);
                    if (message is not null)
                    {
                        _tray.BalloonTipTitle = "Claude usage";
                        _tray.BalloonTipText = message;
                        _tray.ShowBalloonTip(5000);
                    }
                    break;
                case FetchStatus.AuthError:
                    _authError = true;
                    break;
                case FetchStatus.Failure:
                    _consecutiveFailures++;
                    break;
            }
        }
        finally
        {
            _polling = false;
            UpdateTray();
        }
    }

    private void UpdateTray()
    {
        var percent = _snapshot?.SessionPercent;
        var icon = IconRenderer.Render(
            IconRenderer.IconText(percent, _authError),
            IconRenderer.ColorFor(percent, IsStale, _authError));
        var previous = _tray.Icon;
        _tray.Icon = icon;
        previous?.Dispose();

        var tip = Formatting.Tooltip(_snapshot, IsStale, _authError, DateTimeOffset.UtcNow);
        _tray.Text = tip.Length <= 127 ? tip : tip[..127];

        _flyout.UpdateFrom(_snapshot, IsStale, _authError);
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible)
        {
            _flyout.Hide();
        }
        else
        {
            _flyout.UpdateFrom(_snapshot, IsStale, _authError);
            _flyout.ShowNearTray();
        }
    }

    private void OnAutostartToggled(object? sender, EventArgs e)
    {
        if (_autostartItem.Checked) Autostart.Enable(Environment.ProcessPath!);
        else Autostart.Disable();
        _settings.StartWithWindows = _autostartItem.Checked;
        _settings.Save(Settings.DefaultPath);
    }

    private void OnIntervalSelected(object? sender, EventArgs e)
    {
        var item = (ToolStripMenuItem)sender!;
        var seconds = (int)item.Tag!;
        _settings.PollIntervalSeconds = seconds;
        _settings.Save(Settings.DefaultPath);
        _timer.Interval = seconds * 1000;
        foreach (ToolStripMenuItem other in _intervalMenu.DropDownItems)
        {
            other.Checked = Equals(other.Tag, seconds);
        }
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _timer.Dispose();
        _flyout.Dispose();
        base.ExitThreadCore();
    }
}
```

- [ ] **Step 2: Replace Program.cs**

`src/ClaudeWidget/Program.cs`:
```csharp
namespace ClaudeWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\ClaudeWidget_SingleInstance", out var createdNew);
        if (!createdNew) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
```

- [ ] **Step 3: Build and run tests**

Run: `dotnet test`
Expected: build succeeds, all tests PASS.

- [ ] **Step 4: Manual smoke test**

Run: `dotnet run --project src/ClaudeWidget` (app stays running; launch it in the background).

Checklist (report results; the user's real credentials file exists, so live data should appear within seconds):
- Tray icon appears in the notification area showing a number with green/amber/red (or gray "?" briefly before first fetch).
- Hover shows tooltip like `Claude — session 42%, resets in 2h 13m · wk 31%`.
- Left-click opens the flyout with two bars and countdowns; clicking elsewhere closes it.
- Right-click menu: **Refresh now** updates the "Updated" time in the flyout; **Poll interval** shows a checkmark on the current value; changing it writes `%APPDATA%\ClaudeWidget\settings.json`.
- **Start with Windows** toggle creates/removes value `ClaudeWidget` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (verify with `Get-ItemProperty`).
- Launching a second instance exits immediately (single tray icon remains).
- **Exit** removes the tray icon and the process ends.

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m "feat: tray app context and program wiring"
```

---

### Task 13: Publish single-file exe + README

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: complete app (Task 12).
- Produces: distributable `publish/ClaudeWidget.exe`.

- [ ] **Step 1: Publish**

Run:
```powershell
dotnet publish src/ClaudeWidget/ClaudeWidget.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```
Expected: `publish\ClaudeWidget.exe` exists.

- [ ] **Step 2: Verify the published exe runs**

Run: `Start-Process publish\ClaudeWidget.exe`, confirm the tray icon appears with live data, then exit via the tray menu.

- [ ] **Step 3: Write README**

`README.md`:
```markdown
# ClaudeWidget

Windows system-tray widget showing your Claude rate-limit usage at a glance:
session (5-hour window) % on the icon, with a click flyout showing session and
weekly usage bars and reset countdowns.

Inspired by [clawdometer-eink](https://github.com/nsyll/clawdometer-eink).

## Requirements

- Windows 10/11
- Claude Code installed and logged in (the widget reads the OAuth token from
  `%USERPROFILE%\.claude\.credentials.json`; it never writes to it)

## Build & run

```powershell
dotnet run --project src/ClaudeWidget          # run from source
dotnet test                                    # run unit tests
dotnet publish src/ClaudeWidget/ClaudeWidget.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The published `publish\ClaudeWidget.exe` is fully self-contained.

## Usage

- **Tray icon**: session usage %. Green < 70, amber 70–89, red ≥ 90, gray = stale/no data.
- **Left-click**: flyout with session + weekly bars and reset times.
- **Right-click**: Refresh now · Start with Windows · Poll interval · Exit.
- Toast notifications fire when session usage crosses 80% and 95%
  (thresholds editable in `%APPDATA%\ClaudeWidget\settings.json`, applied on restart).

## How it gets the data

Polls Anthropic's OAuth usage endpoint (no tokens consumed). If that fails, it
falls back to a minimal probe request and reads the
`anthropic-ratelimit-unified-*` response headers — the approach used by
clawdometer-eink.
```

- [ ] **Step 4: Commit**

```powershell
git add -A && git commit -m "docs: README and publish verification"
```
