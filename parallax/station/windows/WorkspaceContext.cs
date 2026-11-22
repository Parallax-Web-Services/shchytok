using System.IO;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Parallax.Station;

// These contracts contain no WPF or Windows dependencies. Operational state
// remains owned by the service; this journal stores navigation preferences.
public sealed record WorkspaceIdentity(string Origin, long UserId, long TeamId, string Name, bool PersonalTeam = false)
{
    public string Description => Origin;
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Origin}|{UserId}|{TeamId}")));
}
public sealed record ResourceReference(WorkspaceIdentity Workspace, string Capability, string Kind, long Id)
{
    public string Key => $"{Workspace.Key}:{Capability}:{Kind}:{Id}";
}
public sealed class WorkstreamVisit
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool Pinned { get; set; }
    public long SeenEvent { get; set; }
    public DateTimeOffset VisitedAt { get; set; }
}
public sealed class CompanyGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Teams { get; set; } = [];
}
public sealed class NavigationPreferences
{
    public bool ShowNexusApp { get; set; }
    public List<CompanyGroup> CompanyGroups { get; set; } = [];
    public List<string> HiddenTeams { get; set; } = [];
}
public sealed class WorkspaceJournal
{
    public string BrowserLinkMode { get; set; } = "station";
    public string Origin { get; set; } = "https://slipstream.codes";
    public string? SelectedWorkspace { get; set; }
    public Dictionary<string,NavigationPreferences> Navigation { get; set; } = new();
    public Dictionary<string, List<WorkstreamVisit>> Visits { get; set; } = new();
}
public sealed class WorkspaceStore
{
    private static readonly ConcurrentDictionary<string,WorkspaceStore> Stores = new(StringComparer.OrdinalIgnoreCase);
    public static WorkspaceStore Shared(string directory) => Stores.GetOrAdd(Path.GetFullPath(directory),path => new WorkspaceStore(path));
    private readonly string path;
    public WorkspaceJournal State { get; }
    public WorkspaceStore(string directory)
    {
        path = Path.Combine(directory, "workspace-context.json");
        State = File.Exists(path) ? JsonSerializer.Deserialize<WorkspaceJournal>(File.ReadAllText(path)) ?? new() : new();
    }
    public IReadOnlyList<WorkstreamVisit> Visits(WorkspaceIdentity workspace) => State.Visits.TryGetValue(workspace.Key, out var entries) ? entries : [];
    public WorkstreamVisit Visit(ResourceReference resource, string name)
    {
        if (!State.Visits.TryGetValue(resource.Workspace.Key, out var entries)) State.Visits[resource.Workspace.Key] = entries = [];
        var visit = entries.FirstOrDefault(v => v.Id == resource.Id);
        if (visit is null) { visit = new() { Id = resource.Id }; entries.Add(visit); }
        visit.Name = name; visit.VisitedAt = DateTimeOffset.UtcNow; Save(); return visit;
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(State)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
