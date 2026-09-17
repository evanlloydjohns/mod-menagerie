using ModMenagerie.Domain;

namespace ModMenagerie.Application;

public sealed record SearchPage(Project[] Projects, int Offset, int Total);
public sealed record ReferenceData(string[] GameVersions, string[] Loaders);
public interface IProvider
{
    Task<SearchPage> SearchAsync(string query, int offset, CancellationToken token);
    Task<Project> ProjectAsync(string idOrSlug, CancellationToken token);
    Task<VersionCache> VersionsAsync(string id, CancellationToken token);
    Task<ReferenceData> ReferencesAsync(CancellationToken token);
    Task<ResolvedVersion> VersionAsync(string id, CancellationToken token) => throw new NotSupportedException();
    Task<ResolvedVersion> HashAsync(string hash, string algorithm, CancellationToken token) => throw new NotSupportedException();
}
public interface IStore
{
    IReadOnlyList<Pack> Packs();
    void SavePack(Pack pack);
    void DeletePack(string id);
    IReadOnlyList<Membership> Members(string packId);
    bool Add(Membership membership, Project project);
    void Remove(Membership membership);
    Project? Project(string id);
    VersionCache? Versions(string id);
    Evaluation? Evaluation(Scope scope);
    ManualDecision? Decision(Scope scope);
    void SaveDecision(Scope scope, ManualDecision? decision);
    void SaveRefresh(Project? project, VersionCache? cache, Evaluation evaluation);
    string? Preference(string key);
    void Preference(string key, string value);
    IReadOnlyList<RangeRule> Rules(string packId, string projectId);
    void SaveRule(RangeRule rule);
    void DeleteRule(RangeRule rule);
    void RecordHistory(Pack pack, string kind, ProjectRow[] rows);
    IReadOnlyList<HistoryEntry> History(string packId);
    int Import(Pack pack, bool create, IReadOnlyList<(Project Project, Distribution Form)> projects);
}
public sealed record RefreshProgress(int Completed, int Total, int Succeeded, int Failed, string Project);
public sealed record RefreshResult(int Succeeded, int Failed, int Unprocessed);
