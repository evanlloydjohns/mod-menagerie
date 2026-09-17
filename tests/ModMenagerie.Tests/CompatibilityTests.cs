using ModMenagerie.Domain;
namespace ModMenagerie.Tests;

public class CompatibilityTests
{
    internal static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-09-16T10:00:00Z");
    internal static Release Release(string id = "v1", string channel = "release", string game = "26.3", string loader = "fabric", int days = 0) => new(id, id, "Title says 26.3.x", channel, Time.AddDays(days), [game], [loader]);
    internal static Evidence Evaluate(params Release[] releases) => CompatibilityEngine.Evaluate(new("p", releases, Time, true), "26.3", "fabric", Distribution.Mod);
    [Theory]
    [InlineData("release", Compatibility.Stable)]
    [InlineData("beta", Compatibility.Beta)]
    [InlineData("alpha", Compatibility.Alpha)]
    public void Channels(string channel, Compatibility expected) => Assert.Equal(expected, Evaluate(Release(channel: channel)).Status);
    [Theory]
    [InlineData("26.3", "forge")]
    [InlineData("26.3-pre1", "fabric")]
    [InlineData("26.3.1", "fabric")]
    [InlineData("26.3.x", "fabric")]
    [InlineData("26.3", "quilt")]
    public void ExactMatching(string game, string loader) => Assert.Equal(Compatibility.NotDetected, Evaluate(Release(game: game, loader: loader)).Status);
    [Fact] public void NoCrossReleaseUnion() => Assert.Equal(Compatibility.NotDetected, Evaluate(Release(game: "26.2"), Release(id: "other", loader: "forge")).Status);
    [Fact]
    public void StableFirstThenNewestThenId()
    {
        var evidence = Evaluate(Release("z"), Release("a"), Release("beta", "beta", days: 20));
        Assert.Equal("a", evidence.Candidate?.Id);
        Assert.Equal("new", Evaluate(Release(), Release("new", days: 1)).Candidate?.Id);
    }
    [Fact] public void CompleteEmptyIsNotDetected() => Assert.Equal(Compatibility.NotDetected, Evaluate().Status);
    [Fact]
    public void MissingAndMalformedAreUnknown()
    {
        Assert.Equal(Compatibility.Unknown, CompatibilityEngine.Evaluate(null, "26.3", "fabric", Distribution.Mod).Status);
        Assert.Equal(Compatibility.Unknown, Evaluate(Release(channel: "future")).Status);
        Assert.Equal(Compatibility.Unknown, Evaluate(Release() with
        {
            Valid = false
        }).Status);
        Assert.Equal(Compatibility.Unknown, CompatibilityEngine.Evaluate(new("p", [], Time, false), "26.3", "fabric", Distribution.Mod).Status);
    }
    [Fact] public void DatapackDoesNotRequireFabric() => Assert.Equal(Compatibility.Stable, CompatibilityEngine.Evaluate(new("p", [Release(loader: "datapack")], Time, true), "26.3", "fabric", Distribution.Datapack).Status);
    internal static ProjectRow Row(Compatibility status, bool manual = false) => new(new("pack", "p", Distribution.Mod), new() { Id = "p", Lifecycle = "archived" }, new(new("pack", "p", "26.3", "fabric", Distribution.Mod), new(status, "fixture", null, Time), Time, null, null), manual ? new(new("pack", "p", "26.3", "fabric", Distribution.Mod), status, "note", null, Time) : null, Evaluate(), null);
    [Fact]
    public void ReadinessBuckets()
    {
        var r = Readiness.Calculate(new[] { Compatibility.Stable, Compatibility.Stable, Compatibility.Stable, Compatibility.Stable, Compatibility.Beta, Compatibility.Beta, Compatibility.Alpha, Compatibility.NotDetected, Compatibility.Unknown, Compatibility.Ignored }.Select(s => Row(s, s == Compatibility.Ignored)));
        Assert.Equal(7, r.Compatible);
        Assert.Equal(9, r.Included);
        Assert.Equal(2, r.Blockers);
        Assert.Equal(77.777777, r.Percent!.Value, 5);
        Assert.Equal(1, r.Manual);
    }
    [Fact]
    public void EmptyAndIgnoredAreNot100()
    {
        Assert.Null(Readiness.Calculate([]).Percent);
        Assert.Null(Readiness.Calculate([Row(Compatibility.Ignored)]).Percent);
    }
    [Fact] public void LifecycleDoesNotEraseCompatibility() => Assert.Equal(Compatibility.Stable, Row(Compatibility.Stable).Effective);
}
