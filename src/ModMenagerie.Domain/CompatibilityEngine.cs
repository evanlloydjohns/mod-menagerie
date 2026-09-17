namespace ModMenagerie.Domain;

public static class CompatibilityEngine
{
    public static Evidence Evaluate(VersionCache? cache, string target, string loader, Distribution form)
    {
        var time = cache?.Retrieved ?? DateTimeOffset.UtcNow;
        Evidence Unknown(string why) => new(Compatibility.Unknown, why, null, time);
        if (cache == null)
            return Unknown("Not checked. Refresh to retrieve version evidence.");
        if (!cache.Complete || cache.Releases.Any(v => !v.Valid || string.IsNullOrWhiteSpace(v.Id) || v.Published == default || v.GameVersions == null || v.Loaders == null || v.Channel is not ("release" or "beta" or "alpha")))
            return Unknown("Version evidence is incomplete or contains unrecognized required metadata. Retry Refresh.");
        var required = form == Distribution.Datapack ? "datapack" : loader;
        var candidate = cache.Releases.Where(v => v.GameVersions.Contains(target, StringComparer.Ordinal) && v.Loaders.Contains(required, StringComparer.Ordinal))
            .OrderBy(v => v.Channel == "release" ? 0 : v.Channel == "beta" ? 1 : 2)
            .ThenByDescending(v => v.Published).ThenBy(v => v.Id, StringComparer.Ordinal).FirstOrDefault();
        return candidate == null
            ? new(Compatibility.NotDetected, $"Complete check found no release explicitly listing {target} and {required}. This does not prove it cannot work.", null, time)
            : new(candidate.Channel == "release" ? Compatibility.Stable : candidate.Channel == "beta" ? Compatibility.Beta : Compatibility.Alpha,
                $"Release {candidate.Id} explicitly lists Minecraft {target} and {required}.", candidate, time);
    }
    public static bool Positive(Compatibility status) => status is Compatibility.Stable or Compatibility.Beta or Compatibility.Alpha;
    public static string Label(Compatibility status) => status switch
    {
        Compatibility.Stable => "Stable compatible",
        Compatibility.Beta => "Beta compatible",
        Compatibility.Alpha => "Alpha compatible",
        Compatibility.NotDetected => "Not detected as compatible",
        Compatibility.NotCompatible => "Not compatible",
        Compatibility.Ignored => "Ignored for this upgrade",
        _ => "Unknown"
    };
}
public sealed record Readiness(int Tracked, int Included, int Compatible, int Stable, int Beta, int Alpha, int NotDetected, int Unknown, int Ignored, int Manual)
{
    public int Blockers => Included - Compatible;
    public double? Percent => Included == 0 ? null : 100.0 * Compatible / Included;
    public static Readiness Calculate(IEnumerable<ProjectRow> source)
    {
        var rows = source.ToArray();
        int Count(Compatibility s) => rows.Count(r => r.Effective == s);
        var ignored = Count(Compatibility.Ignored);
        return new(rows.Length, rows.Length - ignored, rows.Count(r => CompatibilityEngine.Positive(r.Effective)), Count(Compatibility.Stable), Count(Compatibility.Beta), Count(Compatibility.Alpha), Count(Compatibility.NotDetected) + Count(Compatibility.NotCompatible), Count(Compatibility.Unknown), ignored, rows.Count(r => r.Manual));
    }
}
