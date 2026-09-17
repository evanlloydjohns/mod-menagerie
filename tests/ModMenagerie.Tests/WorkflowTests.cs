using Microsoft.Data.Sqlite;
using ModMenagerie.Application;
using ModMenagerie.Domain;
using ModMenagerie.Infrastructure;
namespace ModMenagerie.Tests;

public class WorkflowTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "menagerie-tests-" + Guid.NewGuid().ToString("N"), "test.db");
    private SqliteStore Store() => new(path);
    private sealed class Provider : IProvider
    {
        public HashSet<string> Fail = [];
        public Func<Task>? Before;
        public Task<SearchPage> SearchAsync(string q, int o, CancellationToken t) => throw new NotImplementedException();
        public Task<ReferenceData> ReferencesAsync(CancellationToken t) => throw new NotImplementedException();
        public async Task<Project> ProjectAsync(string id, CancellationToken t)
        {
            if (Before != null)
                await Before();
            t.ThrowIfCancellationRequested();
            if (Fail.Contains(id))
                throw new HttpRequestException("Fixture failure");
            return new()
            {
                Id = id,
                Slug = "renamed-" + id,
                Name = id,
                Lifecycle = "archived"
            };
        }
        public Task<VersionCache> VersionsAsync(string id, CancellationToken t) => Task.FromResult(new VersionCache(id, [CompatibilityTests.Release()], CompatibilityTests.Time, true));
    }
    private static Pack Setup(SqliteStore store, int count = 1)
    {
        var pack = Pack.Create("Fixture pack", "26.2", "26.3", "fabric");
        store.SavePack(pack);
        for (var i = 0; i < count; i++)
            store.Add(new(pack.Id, "p" + i, Distribution.Mod), new()
            {
                Id = "p" + i,
                Name = "Project " + i,
                Slug = "old"
            });
        return pack;
    }
    [Fact]
    public void RestartMembershipsAndDeleteIsolation()
    {
        var s = Store();
        var a = Setup(s);
        var b = Setup(s);
        s.SavePack(b with { Current = "26.1", Target = "26.4", Loader = "forge" });
        Assert.False(s.Add(new(a.Id, "p0", Distribution.Mod), new()
        {
            Id = "p0"
        }));
        s.SavePack(a with
        {
            Name = "Renamed"
        });
        s.Preference("table", "saved");
        var restarted = Store();
        Assert.Equal(2, restarted.Packs().Count);
        Assert.Contains(restarted.Packs(), p => p.Name == "Renamed");
        Assert.Contains(restarted.Packs(), p => p.Id == b.Id && p.Current == "26.1" && p.Target == "26.4" && p.Loader == "forge");
        Assert.Contains(restarted.Packs(), p => p.Id == a.Id && p.Current == "26.2" && p.Target == "26.3" && p.Loader == "fabric");
        Assert.Equal("saved", restarted.Preference("table"));
        restarted.Remove(new(a.Id, "p0", Distribution.Mod));
        Assert.Single(restarted.Members(b.Id));
        restarted.DeletePack(a.Id);
        Assert.Single(restarted.Packs());
        Assert.Single(restarted.Members(b.Id));
    }
    [Theory]
    [InlineData(Compatibility.Stable)]
    [InlineData(Compatibility.Beta)]
    [InlineData(Compatibility.Alpha)]
    [InlineData(Compatibility.NotCompatible)]
    [InlineData(Compatibility.Ignored)]
    public async Task OverridesPersistAndKeepAutomaticEvidence(Compatibility status)
    {
        var s = Store();
        var p = Setup(s);
        var tracker = new Tracker(s, new Provider());
        var member = s.Members(p.Id).Single();
        tracker.SetDecision(p, member, status, "author says family", "https://example.com/evidence");
        await tracker.RefreshAsync(p, null, default);
        var row = new Tracker(Store(), new Provider()).Rows(p).Single();
        Assert.Equal(status, row.Effective);
        Assert.Equal(Compatibility.Stable, row.Evaluation!.Automatic.Status);
        Assert.Equal("author says family", row.Decision!.Note);
        Assert.Equal("https://example.com/evidence", row.Decision.Reference);
        Assert.Null(tracker.Rows(p with
        {
            Target = "26.4"
        }).Single().Decision);
        Assert.Null(tracker.Rows(p with
        {
            Loader = "forge"
        }).Single().Decision);
        Assert.Equal(status, tracker.Rows(p).Single().Effective);
        tracker.SetDecision(p, member, null, "", null);
        Assert.Equal(Compatibility.Stable, tracker.Rows(p).Single().Effective);
    }
    [Fact]
    public void ManualPositiveDoesNotInventRelease()
    {
        var s = Store();
        var p = Setup(s);
        var tracker = new Tracker(s, new Provider());
        tracker.SetDecision(p, s.Members(p.Id).Single(), Compatibility.Stable, "", null);
        var row = tracker.Rows(p).Single();
        Assert.Equal("—", row.Candidate);
        Assert.Null(row.Evaluation);
    }
    [Fact]
    public async Task PartialFailurePreservesLastKnownAndCurrent()
    {
        var s = Store();
        var p = Setup(s, 3);
        var provider = new Provider();
        var tracker = new Tracker(s, provider);
        await tracker.RefreshAsync(p, null, default);
        provider.Fail.Add("p1");
        var result = await tracker.RefreshAsync(p, null, default);
        Assert.Equal(new RefreshResult(2, 1, 0), result);
        var failed = tracker.Rows(p).Single(r => r.Project.Id == "p1");
        Assert.Equal(Compatibility.Unknown, failed.Effective);
        Assert.Equal(Compatibility.Stable, failed.Evaluation!.LastSuccess!.Status);
        Assert.Equal("26.2", s.Packs().Single().Current);
        Assert.Equal("renamed-p0", s.Project("p0")!.Slug);
    }
    [Fact]
    public async Task ContextChangeDuringRefreshCannotLeak()
    {
        var s = Store();
        var p = Setup(s);
        var changed = p with
        {
            Target = "26.4"
        };
        var provider = new Provider { Before = () => { s.SavePack(changed); return Task.CompletedTask; } };
        var tracker = new Tracker(s, provider);
        await tracker.RefreshAsync(p, null, default);
        Assert.Equal(Compatibility.NotDetected, tracker.Rows(changed).Single().Effective);
        Assert.Equal(Compatibility.Stable, tracker.Rows(p).Single().Effective);
        Assert.Equal("26.4", s.Packs().Single().Target);
    }
    [Fact]
    public async Task CancelRetainsCompletedResultsAndReportsUnprocessed()
    {
        var s = Store();
        var p = Setup(s, 100);
        using var cts = new CancellationTokenSource();
        var provider = new Provider();
        int calls = 0;
        provider.Before = () => { if (++calls == 5) cts.Cancel(); return Task.CompletedTask; };
        var tracker = new Tracker(s, provider);
        var result = await tracker.RefreshAsync(p, null, cts.Token);
        Assert.Equal(new RefreshResult(4, 0, 96), result);
        Assert.Equal(4, tracker.Rows(p).Count(r => r.Effective == Compatibility.Stable));
    }
    [Fact]
    public async Task HundredProjectsAndProgress()
    {
        var s = Store();
        var p = Setup(s, 100);
        var progress = new List<RefreshProgress>();
        var tracker = new Tracker(s, new Provider());
        var result = await tracker.RefreshAsync(p, new InlineProgress(progress.Add), default);
        Assert.Equal(100, result.Succeeded);
        Assert.Equal(100, progress.Count);
        Assert.Equal(100, progress.Last().Completed);
        Assert.Equal(100, new Tracker(Store(), new Provider()).Rows(p).Count(r => r.Effective == Compatibility.Stable));
    }
    private sealed class InlineProgress(Action<RefreshProgress> action) : IProgress<RefreshProgress>
    {
        public void Report(RefreshProgress value) => action(value);
    }
    [Fact]
    public async Task RemovedMembershipNotResurrected()
    {
        var s = Store();
        var p = Setup(s);
        var provider = new Provider { Before = () => { s.DeletePack(p.Id); return Task.CompletedTask; } };
        await new Tracker(s, provider).RefreshAsync(p, null, default);
        Assert.Empty(s.Members(p.Id));
    }
    [Fact]
    public void NewerSchemaRefusesWithoutReset()
    {
        _ = Store();
        using (var db = new SqliteConnection("Data Source=" + path))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA user_version=999";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => Store());
    }
    [Theory]
    [InlineData("https://evil.example/mod/sodium")]
    [InlineData("https://modrinth.com/modpack/test")]
    [InlineData("not a url")]
    [InlineData("https://modrinth.com/mod/name/version/x")]
    [InlineData("https://modrinth.com.evil.test/mod/name")]
    public void RejectUnsafeUrls(string url) => Assert.Throws<ArgumentException>(() => Tracker.ParseUrl(url));
    [Fact] public void DatapackUrl() => Assert.Equal(("test", Distribution.Datapack), Tracker.ParseUrl("https://modrinth.com/datapack/test"));
    [Fact]
    public async Task SaveFailureIsNotReportedAsSuccess()
    {
        var s = Store();
        var p = Setup(s);
        using (var db = new SqliteConnection("Data Source=" + path))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TRIGGER fail_save BEFORE INSERT ON evaluations BEGIN SELECT RAISE(ABORT, 'Simulated disk failure'); END";
            cmd.ExecuteNonQuery();
        }
        var progress = new List<RefreshProgress>();
        await Assert.ThrowsAsync<SqliteException>(() => new Tracker(s, new Provider()).RefreshAsync(p, new InlineProgress(progress.Add), default));
        Assert.Empty(progress);
        Assert.Null(s.Versions("p0"));
        Assert.Single(s.Packs());
        Assert.Equal("old", s.Project("p0")!.Slug);
    }
    [Fact]
    public async Task OverlappingRefreshRejected()
    {
        var s = Store();
        var p = Setup(s);
        var entered = new TaskCompletionSource();
        var finish = new TaskCompletionSource();
        var provider = new Provider { Before = async () => { entered.SetResult(); await finish.Task; } };
        var tracker = new Tracker(s, provider);
        var first = tracker.RefreshAsync(p, null, default);
        await entered.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(() => tracker.RefreshAsync(p, null, default));
        finish.SetResult();
        await first;
    }
    [Fact]
    public void OverrideCannotCrossCollectionOrDistribution()
    {
        var s = Store();
        var a = Setup(s);
        var b = Setup(s);
        var tracker = new Tracker(s, new Provider());
        var member = s.Members(a.Id).Single();
        tracker.SetDecision(a, member, Compatibility.Stable, "Scoped", null);
        Assert.Null(tracker.Rows(b).Single().Decision);
        Assert.Null(s.Decision(Tracker.ScopeFor(a, member) with
        {
            Form = Distribution.Datapack
        }));
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path.GetDirectoryName(path)))
            Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
