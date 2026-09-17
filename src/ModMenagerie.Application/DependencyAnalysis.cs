using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModMenagerie.Domain;

namespace ModMenagerie.Application;

public sealed record DependencyLine(int Depth, string Root, string Name, string Relationship, string Result, string? ProjectId = null, Project? Project = null, Distribution Form = Distribution.Mod)
{
    public string Tree => new string(' ', Depth * 3) + Name;
}
public sealed record DependencyReport(string Fingerprint, DateTimeOffset Time, int Included, int Ready, DependencyLine[] Lines);

public sealed class DependencyAnalysis(Tracker tracker)
{
    public static string Fingerprint(Pack pack, ProjectRow[] rows) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { pack, rows = rows.Select(r => new { r.Membership, r.Project.Id, r.Effective, r.Decision, r.Evaluation }) }))));
    public DependencyReport? Cached(Pack pack)
    {
        var json = tracker.Store.Preference("dependencies:" + pack.Id);
        if (json == null) return null;
        var report = JsonSerializer.Deserialize<DependencyReport>(json);
        return report?.Fingerprint == Fingerprint(pack, tracker.Rows(pack)) ? report : null;
    }
    public async Task<DependencyReport> AnalyzeAsync(Pack pack, IProgress<string>? progress, CancellationToken token)
    {
        var rows = tracker.Rows(pack);
        var fingerprint = Fingerprint(pack, rows);
        var byId = rows.ToDictionary(r => r.Project.Id);
        var lines = new List<DependencyLine>();
        var fetched = new Dictionary<string, ResolvedVersion>();
        var projects = new Dictionary<string, Project>();
        async Task<ResolvedVersion> Version(string id)
        {
            if (!fetched.TryGetValue(id, out var version)) fetched[id] = version = await tracker.Provider.VersionAsync(id, token);
            return version;
        }
        async Task<bool> Visit(ProjectRow row, string root, int depth, HashSet<string> path)
        {
            token.ThrowIfCancellationRequested();
            void Line(string result) => lines.Add(new(depth, root, row.Name, "Selected candidate", result, row.Project.Id));
            if (depth > 32 || lines.Count >= 6000) { Line("Unknown — analysis bound reached"); return false; }
            if (!CompatibilityEngine.Positive(row.Effective)) { Line("Blocked — project itself is not ready"); return false; }
            if (row.Evaluation?.Automatic.Candidate is not { } candidate) { Line("Unknown — manual compatibility has no selected release"); return false; }
            if (!path.Add(row.Project.Id)) { Line("Unknown — dependency cycle; no automatic assurance"); return false; }
            try
            {
                var dependencies = candidate.Dependencies;
                if (dependencies == null)
                {
                    var resolved = await Version(candidate.Id);
                    if (resolved.ProjectId != row.Project.Id || !resolved.Release.Valid) throw new InvalidDataException("Candidate identity or metadata is inconsistent.");
                    dependencies = resolved.Release.Dependencies;
                }
                if (dependencies == null) { Line("Unknown — dependency metadata is unavailable"); return false; }
                var start = lines.Count;
                Line("Checking");
                bool ready = true;
                foreach (var dependency in dependencies)
                {
                    token.ThrowIfCancellationRequested();
                    if (lines.Count >= 6000) { ready = false; Line("Unknown — analysis bound reached"); break; }
                    var id = dependency.ProjectId;
                    var label = dependency.FileName ?? id ?? dependency.VersionId ?? "Unidentified dependency";
                    if (string.IsNullOrWhiteSpace(label)) label = "Unidentified dependency";
                    if (dependency.Kind is "optional" or "embedded")
                    {
                        lines.Add(new(depth + 1, root, label, dependency.Kind, "Informational — does not block readiness", id));
                        continue;
                    }
                    if (dependency.Kind is not ("required" or "incompatible"))
                    {
                        lines.Add(new(depth + 1, root, label, dependency.Kind, "Unknown relationship — blocks assurance", id)); ready = false; continue;
                    }
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(dependency.VersionId))
                        {
                            var exact = await Version(dependency.VersionId);
                            if (!string.IsNullOrWhiteSpace(id) && id != exact.ProjectId) throw new InvalidDataException("Dependency IDs disagree.");
                            id = exact.ProjectId;
                        }
                        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Dependency has no resolvable identity.");
                        byId.TryGetValue(id, out var tracked);
                        var present = tracked != null && tracked.Effective != Compatibility.Ignored;
                        if (dependency.Kind == "incompatible")
                        {
                            var conflict = present && (string.IsNullOrWhiteSpace(dependency.VersionId) || tracked!.Evaluation?.Automatic.Candidate?.Id == dependency.VersionId);
                            var unknown = present && !string.IsNullOrWhiteSpace(dependency.VersionId) && tracked!.Evaluation?.Automatic.Candidate == null;
                            lines.Add(new(depth + 1, root, tracked?.Name ?? id, "incompatible", conflict ? "Blocked — conflicting included project/release" : unknown ? "Unknown — no selected release to check conflict" : "No matching included conflict", id));
                            ready &= !conflict && !unknown;
                            continue;
                        }
                        if (!present)
                        {
                            if (!projects.TryGetValue(id, out var project)) projects[id] = project = await tracker.Provider.ProjectAsync(id, token);
                            var form = project.Loaders.Contains(pack.Loader) ? Distribution.Mod : project.Loaders.Contains("datapack") ? Distribution.Datapack : Distribution.Mod;
                            lines.Add(new(depth + 1, root, project.Name, "required", tracked == null ? "Blocked — untracked; add explicitly, then refresh" : "Blocked — ignored required dependency", id, tracked == null ? project : null, form));
                            ready = false;
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(dependency.VersionId) && tracked!.Evaluation?.Automatic.Candidate?.Id != dependency.VersionId)
                        {
                            lines.Add(new(depth + 1, root, tracked!.Name, "required", $"Blocked — requires release {dependency.VersionId}; selected {tracked.Evaluation?.Automatic.Candidate?.Id ?? "none"}", id)); ready = false; continue;
                        }
                        lines.Add(new(depth + 1, root, tracked!.Name, "required", "Inspecting selected target release", id));
                        ready &= await Visit(tracked, root, depth + 2, path);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException && !token.IsCancellationRequested)
                    { lines.Add(new(depth + 1, root, label, dependency.Kind, "Unknown — " + ex.Message, id)); ready = false; }
                }
                lines[start] = lines[start] with { Result = ready ? "Ready — declared required dependencies satisfied" : "Blocked or unknown dependencies" };
                return ready;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException && !token.IsCancellationRequested)
            { Line("Unknown — " + ex.Message); return false; }
            finally { path.Remove(row.Project.Id); }
        }
        int good = 0;
        var included = rows.Where(r => r.Effective != Compatibility.Ignored).ToArray();
        foreach (var row in included)
        {
            progress?.Report("Checking dependencies: " + row.Name);
            if (await Visit(row, row.Name, 0, [])) good++;
        }
        var report = new DependencyReport(fingerprint, DateTimeOffset.UtcNow, included.Length, good, lines.ToArray());
        // An edited target/membership/override cannot receive a stale report.
        var current = tracker.Store.Packs().FirstOrDefault(p => p.Id == pack.Id);
        if (current == pack && Fingerprint(current, tracker.Rows(current)) == fingerprint)
            tracker.Store.Preference("dependencies:" + pack.Id, JsonSerializer.Serialize(report));
        return report;
    }
}
