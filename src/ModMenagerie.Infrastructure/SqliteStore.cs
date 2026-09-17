using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModMenagerie.Application;
using ModMenagerie.Domain;

namespace ModMenagerie.Infrastructure;

public sealed class SqliteStore : IStore
{
    private readonly string connectionString;
    public SqliteStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        using var db = Open();
        using var transaction = db.BeginTransaction();
        var version = Convert.ToInt32(Command(db, transaction, "PRAGMA user_version").ExecuteScalar());
        if (version > 1)
            throw new InvalidDataException("This database was created by a newer app. Use that version; the database has not been reset.");
        if (version == 0)
            Command(db, transaction, """
                CREATE TABLE packs(id TEXT PRIMARY KEY, data TEXT NOT NULL);
                CREATE TABLE projects(id TEXT PRIMARY KEY, data TEXT NOT NULL);
                CREATE TABLE memberships(pack TEXT NOT NULL REFERENCES packs(id) ON DELETE CASCADE, project TEXT NOT NULL REFERENCES projects(id), form INTEGER NOT NULL, PRIMARY KEY(pack,project));
                CREATE TABLE versions(project TEXT PRIMARY KEY REFERENCES projects(id), data TEXT NOT NULL);
                CREATE TABLE evaluations(pack TEXT NOT NULL, project TEXT NOT NULL, target TEXT NOT NULL, loader TEXT NOT NULL, form INTEGER NOT NULL, data TEXT NOT NULL, PRIMARY KEY(pack,project,target,loader,form), FOREIGN KEY(pack,project) REFERENCES memberships(pack,project) ON DELETE CASCADE);
                CREATE TABLE overrides(pack TEXT NOT NULL, project TEXT NOT NULL, target TEXT NOT NULL, loader TEXT NOT NULL, form INTEGER NOT NULL, data TEXT NOT NULL, PRIMARY KEY(pack,project,target,loader,form), FOREIGN KEY(pack,project) REFERENCES memberships(pack,project) ON DELETE CASCADE);
                CREATE TABLE preferences(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                PRAGMA user_version=1;
                """).ExecuteNonQuery();
        transaction.Commit();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params object?[] args)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        for (int i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("$" + i, args[i] ?? DBNull.Value);
        return cmd;
    }
    private T? Read<T>(string sql, params object?[] args)
    {
        using var db = Open();
        using var cmd = Command(db, null, sql, args);
        return cmd.ExecuteScalar() is string value ? JsonSerializer.Deserialize<T>(value) : default;
    }
    private static string Json<T>(T item) => JsonSerializer.Serialize(item);
    public IReadOnlyList<Pack> Packs()
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT data FROM packs ORDER BY id");
        using var reader = cmd.ExecuteReader();
        var result = new List<Pack>();
        while (reader.Read())
            result.Add(JsonSerializer.Deserialize<Pack>(reader.GetString(0))!);
        return result.OrderBy(p => p.Name).ToArray();
    }
    public void SavePack(Pack pack)
    {
        pack.Validate();
        using var db = Open();
        Command(db, null, "INSERT INTO packs VALUES($0,$1) ON CONFLICT(id) DO UPDATE SET data=$1", pack.Id, Json(pack)).ExecuteNonQuery();
    }
    public void DeletePack(string id)
    {
        using var db = Open();
        Command(db, null, "DELETE FROM packs WHERE id=$0", id).ExecuteNonQuery();
    }
    public IReadOnlyList<Membership> Members(string packId)
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT project,form FROM memberships WHERE pack=$0 ORDER BY project", packId);
        using var reader = cmd.ExecuteReader();
        var result = new List<Membership>();
        while (reader.Read())
            result.Add(new(packId, reader.GetString(0), (Distribution)reader.GetInt32(1)));
        return result;
    }
    public bool Add(Membership membership, Project project)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Command(db, tx, "INSERT INTO projects VALUES($0,$1) ON CONFLICT(id) DO UPDATE SET data=$1", project.Id, Json(project)).ExecuteNonQuery();
        var added = Command(db, tx, "INSERT OR IGNORE INTO memberships VALUES($0,$1,$2)", membership.PackId, membership.ProjectId, (int)membership.Form).ExecuteNonQuery() == 1;
        tx.Commit();
        return added;
    }
    public void Remove(Membership m)
    {
        using var db = Open();
        Command(db, null, "DELETE FROM memberships WHERE pack=$0 AND project=$1", m.PackId, m.ProjectId).ExecuteNonQuery();
    }
    public Project? Project(string id) => Read<Project>("SELECT data FROM projects WHERE id=$0", id);
    public VersionCache? Versions(string id) => Read<VersionCache>("SELECT data FROM versions WHERE project=$0", id);
    private const string WhereScope = " WHERE pack=$0 AND project=$1 AND target=$2 AND loader=$3 AND form=$4";
    private static object[] Args(Scope s) => [s.PackId, s.ProjectId, s.Target, s.Loader, (int)s.Form];
    public Evaluation? Evaluation(Scope s) => Read<Evaluation>("SELECT data FROM evaluations" + WhereScope, Args(s));
    public ManualDecision? Decision(Scope s) => Read<ManualDecision>("SELECT data FROM overrides" + WhereScope, Args(s));
    public void SaveDecision(Scope scope, ManualDecision? decision)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Command(db, tx, "DELETE FROM overrides" + WhereScope, Args(scope)).ExecuteNonQuery();
        if (decision != null)
            Command(db, tx, "INSERT INTO overrides VALUES($0,$1,$2,$3,$4,$5)", [.. Args(scope), Json(decision)]).ExecuteNonQuery();
        tx.Commit();
    }
    public void SaveRefresh(Project? project, VersionCache? cache, Evaluation evaluation)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        // Removed memberships/packs must not be resurrected by late network responses.
        if (Command(db, tx, "SELECT 1 FROM memberships WHERE pack=$0 AND project=$1 AND form=$2", evaluation.Scope.PackId, evaluation.Scope.ProjectId, (int)evaluation.Scope.Form).ExecuteScalar() == null)
            return;
        if (project != null)
            Command(db, tx, "UPDATE projects SET data=$1 WHERE id=$0", project.Id, Json(project)).ExecuteNonQuery();
        if (cache != null && cache.Complete)
            Command(db, tx, "INSERT INTO versions VALUES($0,$1) ON CONFLICT(project) DO UPDATE SET data=$1", cache.ProjectId, Json(cache)).ExecuteNonQuery();
        Command(db, tx, "INSERT INTO evaluations VALUES($0,$1,$2,$3,$4,$5) ON CONFLICT(pack,project,target,loader,form) DO UPDATE SET data=$5", [.. Args(evaluation.Scope), Json(evaluation)]).ExecuteNonQuery();
        tx.Commit();
    }
    public string? Preference(string key)
    {
        using var db = Open();
        return Command(db, null, "SELECT value FROM preferences WHERE key=$0", key).ExecuteScalar() as string;
    }
    public void Preference(string key, string value)
    {
        using var db = Open();
        Command(db, null, "INSERT INTO preferences VALUES($0,$1) ON CONFLICT(key) DO UPDATE SET value=$1", key, value).ExecuteNonQuery();
    }
}
