using System.Collections.Concurrent;
using ModMenagerie.Domain;

namespace ModMenagerie.Application;

public sealed class Tracker(IStore store, IProvider provider)
{
    private readonly ConcurrentDictionary<string, byte> refreshing = new();
    public IStore Store => store;
    public IProvider Provider => provider;
    public static Scope ScopeFor(Pack pack, Membership member) => new(pack.Id, member.ProjectId, pack.Target, pack.Loader, member.Form);
    public ProjectRow[] Rows(Pack pack) => store.Members(pack.Id).Select(m =>
    {
        var p = store.Project(m.ProjectId)!;
        var scope = ScopeFor(pack, m);
        var cache = store.Versions(m.ProjectId);
        var evaluation = store.Evaluation(scope);
        // A new context can use complete dated metadata, but never an old scoped result.
        if (evaluation == null && cache != null)
        {
            var evidence = CompatibilityEngine.Evaluate(cache, pack.Target, pack.Loader, m.Form);
            evaluation = new(scope, evidence, cache.Retrieved, evidence.Status == Compatibility.Unknown ? null : evidence, null);
        }
        return new ProjectRow(m, p, evaluation, store.Decision(scope), CompatibilityEngine.Evaluate(cache, pack.Current, pack.Loader, m.Form),
            cache?.Releases.Where(r => r.Valid).OrderByDescending(r => r.Published).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault());
    }).ToArray();

    public void SetDecision(Pack pack, Membership member, Compatibility? status, string note, string? reference)
    {
        if (!string.IsNullOrWhiteSpace(reference) && (!Uri.TryCreate(reference, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            throw new ArgumentException("Evidence must be an absolute http or https URL.");
        if (status is Compatibility.Unknown or Compatibility.NotDetected)
            throw new ArgumentException("Choose a supported manual status.");
        var scope = ScopeFor(pack, member);
        store.SaveDecision(scope, status == null ? null : new(scope, status.Value, note, string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), DateTimeOffset.UtcNow));
    }

    public async Task<RefreshResult> RefreshAsync(Pack pack, IProgress<RefreshProgress>? progress, CancellationToken token)
    {
        if (!refreshing.TryAdd(pack.Id, 0))
            throw new InvalidOperationException("This modpack is already refreshing.");
        try
        {
            var members = store.Members(pack.Id).ToArray();
            int good = 0, bad = 0;
            // Sequential per-project requests deliberately bound traffic; I/O remains asynchronous.
            foreach (var member in members)
            {
                if (token.IsCancellationRequested)
                    break;
                var scope = ScopeFor(pack, member); // Immutable context snapshot protects edits during refresh.
                var previous = store.Evaluation(scope)?.LastSuccess;
                if (previous == null && store.Versions(member.ProjectId) is { } savedCache)
                {
                    var cached = CompatibilityEngine.Evaluate(savedCache, pack.Target, pack.Loader, member.Form);
                    if (cached.Status != Compatibility.Unknown)
                        previous = cached;
                }
                var attempt = DateTimeOffset.UtcNow;
                Project? project = null;
                VersionCache? cache = null;
                Evaluation evaluation;
                try
                {
                    project = await provider.ProjectAsync(member.ProjectId, token);
                    cache = await provider.VersionsAsync(member.ProjectId, token);
                    var evidence = CompatibilityEngine.Evaluate(cache, pack.Target, pack.Loader, member.Form);
                    evaluation = new(scope, evidence, attempt, evidence.Status == Compatibility.Unknown ? previous : evidence,
                        evidence.Status == Compatibility.Unknown ? evidence.Reason : null);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidDataException or OperationCanceledException)
                {
                    evaluation = new(scope, new(Compatibility.Unknown, "Check failed. Retry Refresh; last-known evidence is retained.", null, attempt), attempt, previous, ex.Message);
                }
                // Storage errors escape: never count unsaved work as successful.
                store.SaveRefresh(project, cache, evaluation);
                if (evaluation.Error == null)
                    good++;
                else
                    bad++;
                progress?.Report(new(good + bad, members.Length, good, bad, project?.Name ?? member.ProjectId));
            }
            return new(good, bad, members.Length - good - bad);
        }
        finally { refreshing.TryRemove(pack.Id, out _); }
    }

    public static (string Slug, Distribution Form) ParseUrl(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var url) || url.Scheme != "https" || (url.Host != "modrinth.com" && url.Host != "www.modrinth.com") || !url.IsDefaultPort || url.UserInfo.Length > 0)
            throw new ArgumentException("Paste an https://modrinth.com/mod/name or /datapack/name project URL.");
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] is not ("mod" or "datapack") || !System.Text.RegularExpressions.Regex.IsMatch(parts[1], "^[A-Za-z0-9_-]+$"))
            throw new ArgumentException("Use the project's main Modrinth mod or datapack page, not a version or another project type.");
        return (parts[1], parts[0] == "datapack" ? Distribution.Datapack : Distribution.Mod);
    }
}
