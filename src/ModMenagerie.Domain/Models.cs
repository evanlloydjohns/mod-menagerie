namespace ModMenagerie.Domain;

public enum Compatibility
{
    Unknown, Stable, Beta, Alpha, NotDetected, NotCompatible, Ignored
}
public enum Distribution
{
    Mod, Datapack
}
public sealed record Pack(string Id, string Name, string Current, string Target, string Loader, DateTimeOffset Created, DateTimeOffset Updated)
{
    public override string ToString() => Name;
    public static Pack Create(string name, string current, string target, string loader) =>
        new(Guid.NewGuid().ToString("N"), name.Trim(), current.Trim(), target.Trim(), loader.Trim().ToLowerInvariant(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    public void Validate()
    {
        if (new[] { Name, Current, Target, Loader }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Name, current version, target version and loader are required.");
    }
}
public sealed record Membership(string PackId, string ProjectId, Distribution Form);
public sealed record Scope(string PackId, string ProjectId, string Target, string Loader, Distribution Form);
public sealed record Release(string Id, string Number, string Name, string Channel, DateTimeOffset Published, string[] GameVersions, string[] Loaders, bool Valid = true);
public sealed record Project
{
    public override string ToString() => Name;
    public string Id { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Body { get; init; } = "";
    public string Type { get; init; } = "mod";
    public string Authors { get; init; } = "Unavailable";
    public string? Icon
    {
        get; init;
    }
    public string Lifecycle { get; init; } = "unknown";
    public string License { get; init; } = "Unavailable";
    public string[] Categories { get; init; } = [];
    public string[] Loaders { get; init; } = [];
    public string[] GameVersions { get; init; } = [];
    public Dictionary<string, string> Links { get; init; } = [];
    public DateTimeOffset? Updated
    {
        get; init;
    }
    public DateTimeOffset? Retrieved
    {
        get; init;
    }
    public long Downloads
    {
        get; init;
    }
    public string Url => $"https://modrinth.com/{(Type == "datapack" ? "datapack" : "mod")}/{Slug}";
}
public sealed record VersionCache(string ProjectId, Release[] Releases, DateTimeOffset Retrieved, bool Complete);
public sealed record Evidence(Compatibility Status, string Reason, Release? Candidate, DateTimeOffset Time);
public sealed record Evaluation(Scope Scope, Evidence Automatic, DateTimeOffset Attempted, Evidence? LastSuccess, string? Error);
public sealed record ManualDecision(Scope Scope, Compatibility Status, string Note, string? Reference, DateTimeOffset Modified);
public sealed record ProjectRow(Membership Membership, Project Project, Evaluation? Evaluation, ManualDecision? Decision, Evidence Current, Release? Latest)
{
    public override string ToString() => Name + " — " + Status;
    public Compatibility Effective => Decision?.Status ?? Evaluation?.Automatic.Status ?? Compatibility.Unknown;
    public bool Manual => Decision != null;
    public string Name => Project.Name;
    public string Status => CompatibilityEngine.Label(Effective) + (Manual ? " · Manual" : "");
    public string Type => Membership.Form.ToString();
    public string Candidate => Evaluation?.Automatic.Candidate?.Number ?? "—";
    public DateTimeOffset? CandidateDate => Evaluation?.Automatic.Candidate?.Published;
    public DateTimeOffset? Updated => Project.Updated;
    public long Downloads => Project.Downloads;
    public string Authors => Project.Authors;
    public string Loaders => string.Join(", ", Project.Loaders);
    public string GameVersions => string.Join(", ", Project.GameVersions);
    public string License => Project.License;
    public string Lifecycle => Project.Lifecycle;
    public string LatestVersion => Latest?.Number ?? "—";
    public string Freshness => Evaluation == null ? "Not checked" : Evaluation.Error != null ? "Check failed · Unknown" : $"Cached · {Evaluation.LastSuccess?.Time.ToLocalTime():g}";
    public string SearchText => string.Join(" ", Name, Project.Description, Authors, Project.Id, Project.Slug, string.Join(" ", Project.Categories), License, Loaders, string.Join(" ", Project.Links.Select(x => x.Key + " " + x.Value)));
}
