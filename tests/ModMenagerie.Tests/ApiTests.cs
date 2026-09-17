using System.Net;
using ModMenagerie.Infrastructure;
using ModMenagerie.Domain;

namespace ModMenagerie.Tests;

public class ApiTests
{
    private sealed class Handler(Func<int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        public string? Agent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Agent = request.Headers.UserAgent.ToString();
            return Task.FromResult(reply(++Calls));
        }
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    [Fact]
    public async Task ThrottlingAndTransientErrorsRetryBoundedly()
    {
        var handler = new Handler(i => i < 3 ? new(i == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable) : Json("[]"));
        using var api = new ModrinthClient(new HttpClient(handler));
        var cache = await api.VersionsAsync("test", default);
        Assert.True(cache.Complete);
        Assert.Equal(3, handler.Calls);
        Assert.Contains("TheModMenagerie", handler.Agent);
    }
    [Fact]
    public async Task PermanentErrorDoesNotRetry()
    {
        var handler = new Handler(_ => new(HttpStatusCode.NotFound));
        using var api = new ModrinthClient(new HttpClient(handler));
        await Assert.ThrowsAsync<HttpRequestException>(() => api.VersionsAsync("test", default));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task MalformedFieldsYieldUnknown()
    {
        var handler = new Handler(_ => Json("""[{"id":"v","project_id":"test","version_type":"release","date_published":"2026-09-16T10:00:00Z","status":"listed","game_versions":["26.3",null],"loaders":["fabric"]}]"""));
        using var api = new ModrinthClient(new HttpClient(handler));
        var cache = await api.VersionsAsync("test", default);
        Assert.Equal(Compatibility.Unknown, CompatibilityEngine.Evaluate(cache, "26.3", "fabric", Distribution.Mod).Status);
    }
    [Fact]
    public async Task CancelDuringRateLimitWait()
    {
        var handler = new Handler(_ => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.TryAddWithoutValidation("X-Ratelimit-Reset", "300"); return r; });
        using var api = new ModrinthClient(new HttpClient(handler));
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.VersionsAsync("test", cts.Token));
        Assert.Equal(1, handler.Calls);
    }
    [LiveFact]
    public async Task LiveModrinthCheckWhenExplicitlyEnabled()
    {
        using var api = new ModrinthClient();
        var refs = await api.ReferencesAsync(default);
        Assert.Contains("fabric", refs.Loaders);
        Assert.NotEmpty(refs.GameVersions);
        var hits = await api.SearchAsync("sodium", 0, default);
        Assert.NotEmpty(hits.Projects);
        var project = await api.ProjectAsync("sodium", default);
        Assert.False(string.IsNullOrWhiteSpace(project.Id));
        var cache = await api.VersionsAsync(project.Id, default);
        Assert.True(cache.Complete);
        Assert.NotEmpty(cache.Releases);
        Assert.All(cache.Releases, r => Assert.True(r.Valid));
        var candidate = cache.Releases.First(r => r.Loaders.Contains("fabric"));
        Assert.True(CompatibilityEngine.Positive(CompatibilityEngine.Evaluate(cache, candidate.GameVersions[0], "fabric", Distribution.Mod).Status));
        var datapack = await api.ProjectAsync("terralith", default);
        var data = await api.VersionsAsync(datapack.Id, default);
        var release = data.Releases.First(r => r.Loaders.Contains("datapack"));
        Assert.True(CompatibilityEngine.Positive(CompatibilityEngine.Evaluate(data, release.GameVersions[0], "fabric", Distribution.Datapack).Status));
    }
}
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MENAGERIE_LIVE_TEST") != "1")
            Skip = "Set MENAGERIE_LIVE_TEST=1 to run live Modrinth checks.";
    }
}
