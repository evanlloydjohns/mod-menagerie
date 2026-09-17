using System.IO;
using ModMenagerie.Application;
using ModMenagerie.Desktop;
using ModMenagerie.Domain;
using ModMenagerie.Infrastructure;

namespace ModMenagerie.SmokeHarness;

// Development-only manual test host. Never part of the product publish output.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var store = new SqliteStore(Path.Combine(AppContext.BaseDirectory, "smoke-data", "menagerie.db"));
        if (store.Packs().Count == 0)
        {
            var pack = Pack.Create("SYNTHETIC · 100-project UI test", "1.20.1", "1.21.1", "fabric");
            store.SavePack(pack);
            for (int i = 0; i < 100; i++)
                store.Add(new(pack.Id, $"fixture-{i:000}", Distribution.Mod), Fixtures.Project(i));
        }
        var app = new System.Windows.Application();
        app.Run(new MainWindow(new Tracker(store, new Fixtures())) { Title = "The Mod Menagerie — SYNTHETIC TEST ONLY" });
    }
}
internal sealed class Fixtures : IProvider
{
    internal static Project Project(int i) => new() { Id = $"fixture-{i:000}", Slug = $"fixture-{i:000}", Name = $"SYNTHETIC Project {i:000}", Description = "Test fixture, not a real Modrinth project.", Authors = "Test fixture", Loaders = ["fabric"], Categories = [i % 2 == 0 ? "library" : "optimization"], License = "TEST", Lifecycle = "approved" };
    public Task<SearchPage> SearchAsync(string q, int offset, CancellationToken token) => Task.FromResult(new SearchPage([], 0, 0));
    public Task<ReferenceData> ReferencesAsync(CancellationToken token) => Task.FromResult(new ReferenceData(["1.20.1", "1.21.1"], ["fabric"]));
    public async Task<Project> ProjectAsync(string id, CancellationToken token)
    {
        await Task.Delay(450, token);
        int i = int.Parse(id.Split('-')[1]);
        if (i % 11 == 0)
            throw new System.Net.Http.HttpRequestException("Synthetic retrieval failure; retry path is Refresh.");
        return Project(i);
    }
    public Task<VersionCache> VersionsAsync(string id, CancellationToken token)
    {
        int i = int.Parse(id.Split('-')[1]);
        var channel = i % 4 == 0 ? "beta" : i % 4 == 1 ? "alpha" : "release";
        return Task.FromResult(new VersionCache(id, [new(id + "-release", "TEST-1", "Synthetic release", channel, DateTimeOffset.UtcNow, [i % 7 == 0 ? "1.20.1" : "1.21.1"], ["fabric"])], DateTimeOffset.UtcNow, true));
    }
}
