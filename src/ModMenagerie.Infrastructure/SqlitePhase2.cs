using System.Text.Json;
using ModMenagerie.Domain;

namespace ModMenagerie.Infrastructure;

public sealed partial class SqliteStore
{
    public IReadOnlyList<RangeRule> Rules(string packId, string projectId)
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT data FROM range_rules WHERE pack=$0 AND project=$1 ORDER BY release,pattern", packId, projectId);
        using var reader = cmd.ExecuteReader();
        var result = new List<RangeRule>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<RangeRule>(reader.GetString(0))!);
        return result;
    }
    public void SaveRule(RangeRule rule)
    {
        if (!VersionPatterns.IsValid(rule.Pattern) || string.IsNullOrWhiteSpace(rule.Note) || !Uri.TryCreate(rule.Reference, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new ArgumentException("Use a numeric .x family or inclusive range, an HTTPS source, and an evidence note.");
        if (Versions(rule.ProjectId)?.Releases.All(r => r.Id != rule.ReleaseId) != false)
            throw new ArgumentException("Choose a release from the cached provider data first.");
        using var db = Open();
        Command(db, null, "INSERT INTO range_rules VALUES($0,$1,$2,$3,$4,$5,$6) ON CONFLICT(pack,project,release,loader,form,pattern) DO UPDATE SET data=$6", rule.PackId, rule.ProjectId, rule.ReleaseId, rule.Loader, (int)rule.Form, rule.Pattern, Json(rule)).ExecuteNonQuery();
    }
    public void DeleteRule(RangeRule rule)
    {
        using var db = Open();
        Command(db, null, "DELETE FROM range_rules WHERE pack=$0 AND project=$1 AND release=$2 AND loader=$3 AND form=$4 AND pattern=$5", rule.PackId, rule.ProjectId, rule.ReleaseId, rule.Loader, (int)rule.Form, rule.Pattern).ExecuteNonQuery();
    }
    public void RecordHistory(Pack pack, string kind, ProjectRow[] rows)
    {
        var entry = new HistoryEntry(0, kind, DateTimeOffset.UtcNow, pack, rows.Select(r => new HistoryItem(r.Project.Id, r.Name, r.Membership.Form, r.Evaluation?.Automatic.Status ?? Compatibility.Unknown, r.Effective, r.Evaluation?.Automatic.Candidate?.Id, r.Decision == null ? null : r.Decision.Note + " | " + r.Decision.Reference, r.Decision)).ToArray());
        using var db = Open();
        using var tx = db.BeginTransaction();
        if (Command(db, tx, "SELECT 1 FROM packs WHERE id=$0", pack.Id).ExecuteScalar() == null) return;
        Command(db, tx, "INSERT INTO history(pack,time,data) VALUES($0,$1,$2)", pack.Id, entry.Time.ToString("O"), Json(entry)).ExecuteNonQuery();
        // Retain at most 200 observations per collection, and at most one year.
        Command(db, tx, "DELETE FROM history WHERE pack=$0 AND (time<$1 OR id NOT IN (SELECT id FROM history WHERE pack=$0 ORDER BY id DESC LIMIT 200))", pack.Id, DateTimeOffset.UtcNow.AddDays(-365).ToString("O")).ExecuteNonQuery();
        tx.Commit();
    }
    public IReadOnlyList<HistoryEntry> History(string packId)
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT id,data FROM history WHERE pack=$0 AND time>=$1 ORDER BY id DESC LIMIT 200", packId, DateTimeOffset.UtcNow.AddDays(-365).ToString("O"));
        using var reader = cmd.ExecuteReader();
        var result = new List<HistoryEntry>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<HistoryEntry>(reader.GetString(1))! with { Id = reader.GetInt64(0) });
        return result;
    }
    public int Import(Pack pack, bool create, IReadOnlyList<(Project Project, Distribution Form)> projects)
    {
        pack.Validate();
        using var db = Open();
        using var tx = db.BeginTransaction();
        if (create) Command(db, tx, "INSERT INTO packs VALUES($0,$1)", pack.Id, Json(pack)).ExecuteNonQuery();
        else if (Command(db, tx, "SELECT data FROM packs WHERE id=$0", pack.Id).ExecuteScalar() is not string existing || JsonSerializer.Deserialize<Pack>(existing) != pack)
            throw new InvalidOperationException("The destination collection changed. Reopen the import preview.");
        int added = 0;
        foreach (var (project, form) in projects.DistinctBy(p => p.Project.Id))
        {
            if (Command(db, tx, "SELECT 1 FROM memberships WHERE pack=$0 AND project=$1", pack.Id, project.Id).ExecuteScalar() != null) continue;
            Command(db, tx, "INSERT INTO projects VALUES($0,$1) ON CONFLICT(id) DO NOTHING", project.Id, Json(project)).ExecuteNonQuery();
            added += Command(db, tx, "INSERT INTO memberships VALUES($0,$1,$2)", pack.Id, project.Id, (int)form).ExecuteNonQuery();
        }
        tx.Commit();
        return added;
    }
}
