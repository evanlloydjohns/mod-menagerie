using System.Net;
using System.Text.Json;
using ModMenagerie.Application;
using ModMenagerie.Domain;

namespace ModMenagerie.Infrastructure;

public sealed class ModrinthClient : IProvider, IDisposable
{
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset nextRequest;
    private readonly Action<Exception>? log;
    public ModrinthClient(HttpClient? client = null, Action<Exception>? logger = null)
    {
        http = client ?? new HttpClient();
        http.BaseAddress ??= new("https://api.modrinth.com/v2/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TheModMenagerie/1.0 (Windows; local-upgrade-readiness-tracker)");
        log = logger;
    }
    private async Task<JsonDocument> Get(string path, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var wait = nextRequest - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, token);
                try
                {
                    using var response = await http.GetAsync(path, token);
                    double Header(string name) => response.Headers.TryGetValues(name, out var values) && double.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
                    var reset = Header("X-Ratelimit-Reset");
                    if (response.Headers.Contains("X-Ratelimit-Remaining") && Header("X-Ratelimit-Remaining") < 2)
                        nextRequest = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(reset, 1, 3600));
                    if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        if (attempt == 2)
                            throw new HttpRequestException("Modrinth is busy or unavailable. Retry Refresh later.");
                        var retry = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? reset;
                        nextRequest = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(Math.Max(retry, Math.Pow(2, attempt)), 1, 3600));
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new HttpRequestException("Project unavailable on Modrinth (404). The local record has been kept.");
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"Modrinth returned HTTP {(int)response.StatusCode}. Retry later or check the project page.");
                    return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                }
                catch (HttpRequestException ex) when (ex.StatusCode == null && ex.InnerException != null && attempt < 2)
                {
                    log?.Invoke(ex);
                    nextRequest = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, attempt));
                }
                catch (OperationCanceledException ex) when (!token.IsCancellationRequested && attempt < 2)
                {
                    log?.Invoke(ex);
                    nextRequest = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, attempt));
                }
            }
            throw new HttpRequestException("Cannot reach Modrinth. Check your connection and retry.");
        }
        catch (JsonException ex) { log?.Invoke(ex); throw new InvalidDataException("Modrinth returned malformed metadata. Retry later.", ex); }
        catch (HttpRequestException ex) when (ex.InnerException != null) { log?.Invoke(ex); throw new HttpRequestException("Cannot reach Modrinth. Check your connection and retry.", ex); }
        catch (Exception ex) { log?.Invoke(ex); throw; }
        finally { gate.Release(); }
    }
    private static string S(JsonElement e, string key, string fallback = "") => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static string[] A(JsonElement e, string key) => e.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
    private static bool ValidArray(JsonElement e, string key) => e.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array && a.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()));
    private static DateTimeOffset? Date(JsonElement e, string key) => DateTimeOffset.TryParse(S(e, key), out var d) ? d : null;
    private static Project Map(JsonElement e, bool hit)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Modrinth returned malformed project metadata. Retry later.");
        var links = new Dictionary<string, string>();
        foreach (var pair in new[] { ("Source", "source_url"), ("Issues", "issues_url"), ("Wiki", "wiki_url"), ("Community", "discord_url") })
            if (S(e, pair.Item2) is { Length: > 0 } url)
                links[pair.Item1] = url;
        var license = e.TryGetProperty("license", out var l) ? l.ValueKind == JsonValueKind.Object ? S(l, "id", "Unavailable") : l.ValueKind == JsonValueKind.String ? l.GetString()! : "Unavailable" : "Unavailable";
        var id = S(e, hit ? "project_id" : "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("Modrinth project identity is missing.");
        return new Project
        {
            Id = id,
            Slug = S(e, "slug", id),
            Name = S(e, "title", id),
            Description = S(e, "description"),
            Body = S(e, "body"),
            Type = S(e, "project_type"),
            Authors = S(e, "author", "Unavailable"),
            Icon = S(e, "icon_url") is { Length: > 0 } icon ? icon : null,
            License = license,
            Links = links,
            Categories = A(e, "categories").Concat(A(e, "additional_categories")).Distinct().ToArray(),
            Loaders = A(e, "loaders"),
            GameVersions = A(e, "game_versions"),
            Lifecycle = S(e, "status", "unknown"),
            Updated = Date(e, hit ? "date_modified" : "updated"),
            Retrieved = DateTimeOffset.UtcNow,
            Downloads = e.TryGetProperty("downloads", out var d) && d.TryGetInt64(out var count) ? count : 0
        };
    }
    public async Task<SearchPage> SearchAsync(string query, int offset, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Enter a project name to search Modrinth.");
        var facets = Uri.EscapeDataString("[[\"project_type:mod\"]]");
        using var doc = await Get($"search?query={Uri.EscapeDataString(query.Trim())}&offset={offset}&limit=20&facets={facets}", token);
        var root = doc.RootElement;
        return new(root.GetProperty("hits").EnumerateArray().Select(e => Map(e, true)).ToArray(), offset, root.GetProperty("total_hits").GetInt32());
    }
    public async Task<Project> ProjectAsync(string idOrSlug, CancellationToken token)
    {
        using var doc = await Get("project/" + Uri.EscapeDataString(idOrSlug), token);
        var project = Map(doc.RootElement, false);
        if (project.Type is not ("mod" or "datapack"))
            throw new InvalidDataException("Only mods and datapacks can be tracked.");
        // Team information is optional; its failure cannot invalidate version evidence.
        try
        {
            using var team = await Get($"project/{Uri.EscapeDataString(project.Id)}/members", token);
            var authors = team.RootElement.EnumerateArray().Select(m => m.TryGetProperty("user", out var user) ? S(user, "username") + " (" + S(m, "role") + ")" : "").Where(x => x.Length > 0);
            project = project with
            {
                Authors = string.Join(", ", authors)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or InvalidOperationException || ex is OperationCanceledException && !token.IsCancellationRequested) { log?.Invoke(ex); }
        return project;
    }
    public async Task<VersionCache> VersionsAsync(string id, CancellationToken token)
    {
        using var doc = await Get($"project/{Uri.EscapeDataString(id)}/version?include_changelog=false", token);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Version list is incomplete.");
        if (doc.RootElement.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object))
            throw new InvalidDataException("Version list contains malformed entries. Retry later.");
        var versions = doc.RootElement.EnumerateArray().Select(e => MapRelease(e, id)).ToArray();
        return new(id, versions, DateTimeOffset.UtcNow, true);
    }
    private static Release MapRelease(JsonElement e, string projectId)
    {
        Dependency[]? dependencies = null;
        if (e.TryGetProperty("dependencies", out var array) && array.ValueKind == JsonValueKind.Array && array.EnumerateArray().All(d => d.ValueKind == JsonValueKind.Object))
            dependencies = array.EnumerateArray().Select(d => new Dependency(S(d, "project_id"), S(d, "version_id"), S(d, "dependency_type"), S(d, "file_name"))).ToArray();
        return new(S(e, "id"), S(e, "version_number"), S(e, "name"), S(e, "version_type"), Date(e, "date_published") ?? default,
            A(e, "game_versions"), A(e, "loaders"), S(e, "project_id") == projectId && ValidArray(e, "game_versions") && ValidArray(e, "loaders") && S(e, "status") is "listed" or "archived" or "unlisted", dependencies);
    }
    public async Task<ResolvedVersion> VersionAsync(string id, CancellationToken token)
    {
        using var doc = await Get("version/" + Uri.EscapeDataString(id), token);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || S(root, "id") != id || string.IsNullOrWhiteSpace(S(root, "project_id")))
            throw new InvalidDataException("Version identity is missing or inconsistent.");
        return new(S(root, "project_id"), MapRelease(root, S(root, "project_id")));
    }
    public async Task<ResolvedVersion> HashAsync(string hash, string algorithm, CancellationToken token)
    {
        var length = algorithm == "sha512" ? 128 : algorithm == "sha1" ? 40 : 0;
        if (length == 0 || hash.Length != length || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid file hash.");
        using var doc = await Get($"version_file/{hash}?algorithm={algorithm}&multiple=true", token);
        var root = doc.RootElement;
        var entries = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : [root];
        if (entries.Length != 1 || entries[0].ValueKind != JsonValueKind.Object) throw new InvalidDataException("Hash does not resolve uniquely to one release.");
        var e = entries[0];
        if (string.IsNullOrWhiteSpace(S(e, "project_id")) || string.IsNullOrWhiteSpace(S(e, "id")) || !e.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array ||
            !files.EnumerateArray().Any(f => f.ValueKind == JsonValueKind.Object && f.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Object && string.Equals(S(hashes, algorithm), hash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Resolved release did not confirm the requested hash.");
        return new(S(e, "project_id"), MapRelease(e, S(e, "project_id")));
    }
    public async Task<ReferenceData> ReferencesAsync(CancellationToken token)
    {
        using var games = await Get("tag/game_version", token);
        using var loaders = await Get("tag/loader", token);
        return new(games.RootElement.EnumerateArray().Where(e => S(e, "version_type") == "release").Select(e => S(e, "version")).ToArray(),
            loaders.RootElement.EnumerateArray().Where(e => A(e, "supported_project_types").Contains("mod") && S(e, "name") != "datapack").Select(e => S(e, "name")).ToArray());
    }
    public void Dispose()
    {
        http.Dispose();
        gate.Dispose();
    }
}
