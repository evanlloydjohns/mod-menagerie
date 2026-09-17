using ModMenagerie.Domain;

namespace ModMenagerie.Application;

public sealed record ImportFile(string Path, string? Hash, string Algorithm, string Environment, string? Problem);
public sealed record ImportManifest(string Name, string Current, string Loader, string LoaderVersion, ImportFile[] Files, string[] Warnings);
public sealed class ImportRow(ImportFile file, Project? project, Distribution form, string status, string? versionId = null)
{
    public bool Include { get; set; } = project != null;
    public string Path => file.Path;
    public string Environment => file.Environment;
    public Project? Project { get; } = project;
    public string Name => Project?.Name ?? "Unresolved";
    public Distribution Form { get; } = form;
    public string Status { get; } = status;
    public string DestinationStatus { get; set; } = "New membership";
    public string? VersionId { get; } = versionId;
}
public sealed class PackImport(IProvider provider)
{
    public async Task<ImportRow[]> ResolveAsync(ImportManifest manifest, IProgress<string>? progress, CancellationToken token)
    {
        var rows = new List<ImportRow>();
        var seen = new HashSet<string>();
        var hashes = new Dictionary<string, ResolvedVersion>();
        var projects = new Dictionary<string, Project>();
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Identifying {rows.Count + 1}/{manifest.Files.Length}: {file.Path}");
            try
            {
                if (file.Problem != null || file.Hash == null) throw new InvalidDataException(file.Problem ?? "No supported hash.");
                var key = file.Algorithm + ":" + file.Hash;
                if (!hashes.TryGetValue(key, out var version)) hashes[key] = version = await provider.HashAsync(file.Hash, file.Algorithm, token);
                if (!version.Release.Valid) throw new InvalidDataException("Provider returned incomplete release metadata.");
                if (!projects.TryGetValue(version.ProjectId, out var project)) projects[version.ProjectId] = project = await provider.ProjectAsync(version.ProjectId, token);
                var mod = version.Release.Loaders.Contains(manifest.Loader, StringComparer.Ordinal);
                var datapack = version.Release.Loaders.Contains("datapack", StringComparer.Ordinal);
                if (mod == datapack) throw new InvalidDataException("Distribution is ambiguous or does not match this pack's loader.");
                var duplicate = !seen.Add(project.Id);
                rows.Add(new(file, project, datapack ? Distribution.Datapack : Distribution.Mod, duplicate ? "Duplicate project in archive — skipped" : "Identified by " + file.Algorithm, version.Release.Id) { Include = !duplicate });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or ArgumentException or OperationCanceledException)
            {
                rows.Add(new(file, null, Distribution.Mod, ex.Message));
            }
        }
        return rows.ToArray();
    }
}
