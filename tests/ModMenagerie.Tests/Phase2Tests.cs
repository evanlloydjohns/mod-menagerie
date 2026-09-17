using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModMenagerie.Application;
using ModMenagerie.Domain;
using ModMenagerie.Infrastructure;

namespace ModMenagerie.Tests;

public sealed class Phase2Tests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "menagerie-phase2-" + Guid.NewGuid().ToString("N"));
    private SqliteStore Store() => new(Path.Combine(directory, "test.db"));
    private static Release Release(string id, params Dependency[] deps) => new(id, id, id, "release", DateTimeOffset.Parse("2026-09-16T12:00:00Z"), ["26.3"], ["fabric"], true, deps);
    private static Project Project(string id) => new() { Id = id, Slug = id, Name = id, Loaders = ["fabric"] };
    private static void Add(SqliteStore store, Pack pack, string id, Release release)
    {
        var project = Project(id);
        var member = new Membership(pack.Id, id, Distribution.Mod);
        store.Add(member, project);
        var cache = new VersionCache(id, [release], DateTimeOffset.UtcNow, true);
        var evidence = CompatibilityEngine.Evaluate(cache, pack.Target, pack.Loader, member.Form);
        store.SaveRefresh(project, cache, new(Tracker.ScopeFor(pack, member), evidence, cache.Retrieved, evidence, null));
    }
    private sealed class Provider : IProvider
    {
        public Dictionary<string, ResolvedVersion> Versions = [];
        public int HashCalls;
        public Func<Task>? Before;
        public Task<Project> ProjectAsync(string id, CancellationToken token) => Task.FromResult(Project(id));
        public Task<VersionCache> VersionsAsync(string id, CancellationToken token) => throw new NotImplementedException();
        public Task<SearchPage> SearchAsync(string q, int o, CancellationToken token) => throw new NotImplementedException();
        public Task<ReferenceData> ReferencesAsync(CancellationToken token) => throw new NotImplementedException();
        public async Task<ResolvedVersion> VersionAsync(string id, CancellationToken token)
        {
            if (Before != null)
                await Before();
            token.ThrowIfCancellationRequested();
            return Versions.TryGetValue(id, out var result) ? result : throw new HttpRequestException("Fixture unavailable");
        }
        public Task<ResolvedVersion> HashAsync(string h, string algorithm, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            HashCalls++;
            return h.StartsWith('a') ? Task.FromResult(new ResolvedVersion("a", Release("a1"))) : throw new HttpRequestException("Unresolved fixture hash");
        }
    }
    [Theory]
    [InlineData("26.x", "26.3", true)]
    [InlineData("26.x", "26", false)]
    [InlineData("26.x", "260.3", false)]
    [InlineData("26.3.x", "26.3.1", true)]
    [InlineData("26.3.x", "26.30.1", false)]
    [InlineData("26.3.1–26.3.5", "26.3.1", true)]
    [InlineData("26.3.1-26.3.5", "26.3.5", true)]
    [InlineData("26.3.1-26.3.5", "26.3.6", false)]
    [InlineData("26.3.1-26.3.5", "26.3.2.0", false)]
    [InlineData("26.x", "26.3-pre1", false)]
    [InlineData("26.3.5-26.3.1", "26.3.2", false)]
    [InlineData("26.*", "26.3", false)]
    [InlineData("026.x", "26.3", false)]
    public void NumericRangesHaveExplicitBoundaries(string pattern, string target, bool expected) => Assert.Equal(expected, VersionPatterns.Matches(pattern, target));

    [Fact]
    public void RangesAreReleaseScopedAndKeepManualPrecedenceAcrossRestart()
    {
        var store = Store();
        var pack = Pack.Create("Test", "26.1", "26.4", "fabric");
        store.SavePack(pack);
        Add(store, pack, "a", Release("a1"));
        var rule = new RangeRule(pack.Id, "a", "a1", "fabric", Distribution.Mod, "26.x", "https://example.org/author-evidence", "Explicit support for this release", DateTimeOffset.UtcNow);
        store.SaveRule(rule);
        var tracker = new Tracker(Store(), new Provider());
        Assert.Equal(Compatibility.Stable, tracker.Rows(pack)[0].Effective);
        Assert.Contains(rule.Reference, tracker.Rows(pack)[0].Evaluation!.Automatic.Reason);
        Assert.Equal(Compatibility.NotDetected, tracker.Rows(pack with
        {
            Loader = "forge"
        })[0].Effective);
        tracker.SetDecision(pack, new(pack.Id, "a", Distribution.Mod), Compatibility.NotCompatible, "Manual blocker", null);
        Assert.Equal(Compatibility.NotCompatible, new Tracker(Store(), new Provider()).Rows(pack)[0].Effective);
        store.DeleteRule(rule);
        tracker.SetDecision(pack, new(pack.Id, "a", Distribution.Mod), null, "", null);
        Assert.Equal(Compatibility.NotDetected, tracker.Rows(pack)[0].Effective);
        Assert.Throws<ArgumentException>(() => store.SaveRule(rule with { Reference = "http://example.org" }));
        Assert.Throws<ArgumentException>(() => store.SaveRule(rule with { ReleaseId = "nonexistent" }));
        store.SaveRule(rule);
        store.Remove(new(pack.Id, "a", Distribution.Mod));
        Assert.Empty(Store().Rules(pack.Id, "a"));
    }
    [Fact]
    public async Task RequiredTransitiveCycleOptionalEmbeddedAndConflictsRemainDistinct()
    {
        var store = Store();
        var pack = Pack.Create("Deps", "26.2", "26.3", "fabric");
        store.SavePack(pack);
        Add(store, pack, "a", Release("a1", new Dependency("b", null, "required", null), new("missing", null, "optional", null), new(null, null, "embedded", "bundled.jar")));
        Add(store, pack, "b", Release("b1", new Dependency("c", null, "required", null)));
        Add(store, pack, "c", Release("c1"));
        var tracker = new Tracker(store, new Provider());
        var analysis = new DependencyAnalysis(tracker);
        var ready = await analysis.AnalyzeAsync(pack, null, default);
        Assert.Equal(3, ready.Ready);
        Assert.Equal(2, ready.Lines.Count(l => l.Relationship is "optional" or "embedded"));
        Assert.NotNull(analysis.Cached(pack));
        Add(store, pack, "c", Release("c2", new Dependency("a", null, "required", null)));
        Assert.Null(analysis.Cached(pack));
        var cycle = await analysis.AnalyzeAsync(pack, null, default);
        Assert.Equal(0, cycle.Ready);
        Assert.Contains(cycle.Lines, l => l.Result.Contains("cycle"));
        Add(store, pack, "c", Release("c3", new Dependency("b", null, "incompatible", null)));
        var conflict = await analysis.AnalyzeAsync(pack, null, default);
        Assert.Equal(0, conflict.Ready);
        Assert.Contains(conflict.Lines, l => l.Result.Contains("conflicting"));
    }
    [Fact]
    public async Task UntrackedIgnoredAndVersionSpecificDependenciesBlockWithoutChangingMembership()
    {
        var store = Store();
        var pack = Pack.Create("Deps", "26.2", "26.3", "fabric");
        store.SavePack(pack);
        Add(store, pack, "a", Release("a1", new Dependency(null, "b1", "required", null)));
        var provider = new Provider { Versions = { ["b1"] = new("b", Release("b1")) } };
        var tracker = new Tracker(store, provider);
        var analysis = new DependencyAnalysis(tracker);
        var result = await analysis.AnalyzeAsync(pack, null, default);
        Assert.Equal(0, result.Ready);
        Assert.Single(store.Members(pack.Id));
        Assert.Contains(result.Lines, l => l.Project?.Id == "b");
        Add(store, pack, "b", Release("b2"));
        result = await analysis.AnalyzeAsync(pack, null, default);
        Assert.Equal(1, result.Ready);
        Assert.Contains(result.Lines, l => l.Result.Contains("requires release b1"));
        tracker.SetDecision(pack, new(pack.Id, "b", Distribution.Mod), Compatibility.Ignored, "Ignore", null);
        result = await analysis.AnalyzeAsync(pack, null, default);
        Assert.Equal(0, result.Ready);
        Assert.Equal(1, result.Included);
    }
    [Fact]
    public async Task MissingMetadataManualPositiveAndStaleContextNeverProduceReadyReports()
    {
        var store = Store();
        var pack = Pack.Create("Deps", "26.2", "26.3", "fabric");
        store.SavePack(pack);
        Add(store, pack, "a", Release("a1") with
        {
            Dependencies = null
        });
        var provider = new Provider();
        var tracker = new Tracker(store, provider);
        var analysis = new DependencyAnalysis(tracker);
        Assert.Equal(0, (await analysis.AnalyzeAsync(pack, null, default)).Ready);
        var changed = pack with
        {
            Target = "26.4"
        };
        provider.Before = () => { store.SavePack(changed); return Task.CompletedTask; };
        provider.Versions["a1"] = new("a", Release("a1"));
        await analysis.AnalyzeAsync(pack, null, default);
        Assert.Null(analysis.Cached(changed));
        tracker.SetDecision(changed, new(pack.Id, "a", Distribution.Mod), Compatibility.Stable, "Manual support", null);
        Assert.Equal(0, (await analysis.AnalyzeAsync(changed, null, default)).Ready);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis.AnalyzeAsync(changed, null, canceled.Token));
    }
    private string Archive(string json, bool duplicate = false)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid() + ".mrpack");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            writer.Write(json);
        if (duplicate)
        {
            using var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open());
            writer.Write(json);
        }
        using (var writer = new StreamWriter(zip.CreateEntry("overrides/mods/unidentified.jar").Open()))
            writer.Write("not executable");
        return path;
    }
    private static string Index(params object[] files) => JsonSerializer.Serialize(new { formatVersion = 1, game = "minecraft", name = "Fixture import", dependencies = new Dictionary<string, string> { ["minecraft"] = "26.2", ["fabric-loader"] = "0.18" }, files });
    private static object FileEntry(string path, char hash = 'a') => new { path, hashes = new { sha512 = new string(hash, 128) } };
    [Fact]
    public async Task ImportPreviewResolvesHashesReportsDuplicatesFailuresAndNeverExtracts()
    {
        var path = Archive(Index(FileEntry("mods/a.jar"), FileEntry("mods/duplicate.jar"), FileEntry("mods/unknown.jar", 'b'), FileEntry("../bad.jar")));
        var manifest = MrpackReader.Read(path);
        Assert.Equal("26.2", manifest.Current);
        Assert.Single(manifest.Warnings);
        var provider = new Provider();
        var rows = await new PackImport(provider).ResolveAsync(manifest, null, default);
        Assert.Equal(4, rows.Length);
        Assert.Single(rows, r => r.Include);
        Assert.Equal(2, rows.Count(r => r.Project == null));
        Assert.Equal(2, provider.HashCalls);
        Assert.False(Directory.Exists(Path.Combine(directory, "overrides")));
        Assert.False(System.IO.File.Exists(Path.Combine(directory, "bad.jar")));
    }
    [Fact]
    public void InvalidArchiveAndOversizedIndexAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => MrpackReader.Read(Archive(Index(), true)));
        Assert.Throws<InvalidDataException>(() => MrpackReader.Read(Archive("{}")));
        Assert.Throws<InvalidDataException>(() => MrpackReader.Read(Archive(new string(' ', MrpackReader.MaxIndexBytes + 1))));
    }
    [Fact]
    public void ImportIsAtomicPreservesExistingContextAndDecisionsAndRejectsStaleDestination()
    {
        var store = Store();
        var pack = Pack.Create("Import", "26.2", "26.3", "fabric");
        Assert.Equal(2, store.Import(pack, true, [(Project("a"), Distribution.Mod), (Project("b"), Distribution.Mod)]));
        var tracker = new Tracker(store, new Provider());
        tracker.SetDecision(pack, new(pack.Id, "a", Distribution.Mod), Compatibility.Stable, "Keep me", "https://example.org");
        Assert.Equal(1, store.Import(pack, false, [(Project("a"), Distribution.Datapack), (Project("c"), Distribution.Mod)]));
        Assert.Equal(Distribution.Mod, store.Members(pack.Id).Single(m => m.ProjectId == "a").Form);
        Assert.Equal("Keep me", store.Decision(new(pack.Id, "a", pack.Target, pack.Loader, Distribution.Mod))!.Note);
        Assert.Equal(pack, Store().Packs().Single());
        Assert.Throws<InvalidOperationException>(() => store.Import(pack with { Target = "26.4" }, false, [(Project("d"), Distribution.Mod)]));
        using var db = new SqliteConnection("Data Source=" + Path.Combine(directory, "test.db"));
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TRIGGER stop_import BEFORE INSERT ON memberships WHEN NEW.project='fail' BEGIN SELECT RAISE(ABORT,'fixture'); END";
        cmd.ExecuteNonQuery();
        var newPack = Pack.Create("Rollback", "26.2", "26.3", "fabric");
        Assert.Throws<SqliteException>(() => store.Import(newPack, true, [(Project("first"), Distribution.Mod), (Project("fail"), Distribution.Mod)]));
        Assert.DoesNotContain(store.Packs(), p => p.Id == newPack.Id);
        Assert.Null(store.Project("first"));
    }
    [Fact]
    public void HistoryPreservesContextsMembershipAndOverridesAndIsBounded()
    {
        var store = Store();
        var pack = Pack.Create("History", "26.2", "26.3", "fabric");
        store.SavePack(pack);
        Add(store, pack, "a", Release("a1"));
        var tracker = new Tracker(store, new Provider());
        tracker.Capture(pack, "Membership added");
        tracker.SetDecision(pack, new(pack.Id, "a", Distribution.Mod), Compatibility.NotCompatible, "Reason", "https://example.org");
        var changed = pack with
        {
            Target = "26.4"
        };
        store.SavePack(changed);
        tracker.Capture(changed, "Context changed");
        var entries = Store().History(pack.Id);
        Assert.Equal(3, entries.Count);
        Assert.Equal("26.4", entries[0].Pack.Target);
        Assert.Equal(Compatibility.NotCompatible, entries[1].Items[0].Effective);
        Assert.Equal(Compatibility.Stable, entries[1].Items[0].Automatic);
        Assert.Equal("https://example.org", entries[1].Items[0].Decision!.Reference);
        Assert.Equal(pack.Target, entries[1].Items[0].Decision!.Scope.Target);
        store.Remove(new(pack.Id, "a", Distribution.Mod));
        tracker.Capture(changed, "Membership removed");
        Assert.Empty(store.History(pack.Id)[0].Items);
        for (int i = 0; i < 205; i++)
            tracker.Capture(changed, "Fixture observation");
        Assert.Equal(200, Store().History(pack.Id).Count);
    }
    [Fact]
    public void SchemaOneUpgradesWithoutLosingExistingPack()
    {
        var store = Store();
        var pack = Pack.Create("Legacy", "26.2", "26.3", "fabric");
        store.SavePack(pack);
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(directory, "test.db")))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DROP TABLE history; DROP TABLE range_rules; PRAGMA user_version=1";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(pack, Store().Packs().Single());
        Assert.Empty(Store().History(pack.Id));
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
