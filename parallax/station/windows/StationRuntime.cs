using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Threading;

namespace Parallax.Station;

// Presentation-independent coordination over existing Station services. The
// web shell receives display data and scoped actions, never device credentials.
public sealed class StationRuntime : IDisposable
{
    private SlipstreamClient? client;
    private readonly string directory;
    private long? accountUserId;
    private static event Action<string>? NavigationChanged;
    private string AccountKey => accountUserId is long id && client is not null ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(client.Origin.GetLeftPart(UriPartial.Authority) + "|" + id))) : throw new InvalidOperationException("Connect an account first.");
    private readonly bool isolated;
    private readonly MachineTelemetry telemetry = new();
    private readonly Dictionary<string,WorkspaceIdentity> memberships = new();
    private readonly Dictionary<string,LocalTerminal> terminals = new();
    private readonly Dictionary<string,(string Workspace,long Task,string Action,DateTime Expires)> approvals = new();
    private readonly DispatcherTimer terminalTimer;
    private bool disposed;
    public WorkspaceStore Store { get; }
    public WorkspaceIdentity? Workspace { get; private set; }
    public string ContextKey => Workspace?.Key ?? "local";
    public event Action<object>? Event;
    public StationRuntime(string? directory)
    {
        isolated = directory is not null; this.directory = directory ?? StationServices.StateDirectory; Store = WorkspaceStore.Shared(this.directory);
        NavigationChanged += OnNavigationChanged;
        terminalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        terminalTimer.Tick += (_, _) => DrainTerminals(); terminalTimer.Start();
    }
    internal StationRuntime CreateSibling()
    {
        var sibling = new StationRuntime(isolated ? directory : null) { client = client?.CloneForWindow(), Workspace = Workspace, accountUserId = accountUserId, SharedSignInAvailable = SharedSignInAvailable };
        foreach (var item in memberships) sibling.memberships[item.Key] = item.Value;
        return sibling;
    }
    internal void PauseTerminals(string workspace) { foreach (var t in terminals.Values.Where(t => t.Workspace == workspace)) t.Ready = false; }
    internal void ResumeTerminals(string workspace) { foreach (var t in terminals.Values.Where(t => t.Workspace == workspace)) t.Ready = true; }
    internal void MoveTerminalsTo(StationRuntime target,string workspace)
    {
        foreach (var t in terminals.Values.Where(t => t.Workspace == workspace).ToArray())
        { terminals.Remove(t.Id); t.Ready = false; t.InFlight = false; target.terminals.Add(t.Id,t); }
    }
    internal async Task<string> SharedSessionTicket(string challenge)
    {
        var response = await Api.WriteAsync("station/session",Workspace ?? throw new InvalidOperationException("Connect Station first."),new { challenge });
        string ticket = response.GetProperty("ticket").GetString() ?? "";
        if (ticket.Length != 64 || ticket.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new InvalidOperationException("Invalid shared sign-in response.");
        return ticket;
    }
    internal Uri ApplicationAddress(Uri address,string workspace)
    {
        // Nexus issues the same team IDs in these existing first-party apps.
        // Other integrations have independent identities; never infer a mapping.
        if (!memberships.TryGetValue(workspace,out var identity) || identity.Origin != "https://slipstream.codes" ||
            address.GetLeftPart(UriPartial.Authority) is not ("https://continuum.ceo" or "https://slipstream.codes") ||
            StationServices.IsAuthenticationAddress(address)) return address;
        var builder = new UriBuilder(address);
        var query = builder.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)
            .Where(part => Uri.UnescapeDataString(part.Split('=')[0]) != "workspace_team").ToList();
        query.Add("workspace_team=" + identity.TeamId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Query = string.Join("&",query); return builder.Uri;
    }
    internal bool Connected => client is not null;
    internal bool SharedSignInAvailable { get; private set; }
    internal static bool AdvertisesSharedSignIn(JsonElement me) =>
        me.TryGetProperty("station_sign_in",out var capability) && capability.ValueKind == JsonValueKind.Object &&
        capability.TryGetProperty("available",out var available) && available.ValueKind == JsonValueKind.True &&
        capability.TryGetProperty("nexus_origin",out var origin) && origin.ValueKind == JsonValueKind.String &&
        origin.GetString() == "https://my.parallaxweb.services";
    internal void SetTestClient(SlipstreamClient api) { if (!isolated) throw new InvalidOperationException(); client?.Dispose(); client = api; }
    private SlipstreamClient Api => client ?? throw new InvalidOperationException("Connect Slipstream first.");
    public async Task<object> Bootstrap()
    {
        if (client is null && !isolated)
        {
            string? token = DeviceCredential.Load(Store.State.Origin);
            if (token is not null) { client = new(Store.State.Origin); client.Authenticate(token); }
        }
        if (client is not null)
        {
            HttpRequestException? failure = null;
            for(int attempt=0;attempt<2;attempt++)
            {
                try { return await Discover(); }
                catch(HttpRequestException error) { failure=error; if(attempt==0)await Task.Delay(200); }
            }
            SharedSignInAvailable=false;
            return new { connected=false,origin=Store.State.Origin,organizations=memberships.Values.ToArray(),workspace=Workspace,machine=Environment.MachineName,
                connectionIssue=new { category=(failure as ServiceNetworkException)?.Category ?? "network",message=failure?.Message ?? "Slipstream is unavailable.",savedConnection=true } };
        }
        return new { connected = false, origin = Store.State.Origin, organizations = Array.Empty<object>(), workspace = (object?)null, machine = Environment.MachineName };
    }
    private async Task<object> Discover()
    {
        var session = Api;
        var me = await session.ReadAsync("me");
        if(!ReferenceEquals(client,session))throw new OperationCanceledException("The Station connection changed during discovery.");
        SharedSignInAvailable = AdvertisesSharedSignIn(me);
        long userId = me.GetProperty("user").GetProperty("id").GetInt64(); accountUserId = userId;
        memberships.Clear();
        foreach (var team in me.GetProperty("teams").EnumerateArray())
        {
            var identity = new WorkspaceIdentity(Api.Origin.GetLeftPart(UriPartial.Authority),userId,team.GetProperty("id").GetInt64(),Text(team,"name"),team.TryGetProperty("personal_team",out var personal) && (personal.ValueKind == JsonValueKind.True || personal.ToString() == "1"));
            memberships[identity.Key] = identity;
        }
        Workspace = (Workspace?.Key ?? Store.State.SelectedWorkspace) is string selected && memberships.TryGetValue(selected,out var found) ? found : memberships.Values.FirstOrDefault();
        Store.State.SelectedWorkspace = Workspace?.Key; Store.Save();
        return new { connected = true, origin = Store.State.Origin, user = me.GetProperty("user"), organizations = memberships.Values.ToArray(), workspace = Workspace, navigation = GetNavigation(), machine = Environment.MachineName };
    }
    public async Task<object> Call(string method,JsonElement p)
    {
        if (disposed) throw new ObjectDisposedException(nameof(StationRuntime));
        switch (method)
        {
            case "bootstrap": return await Bootstrap();
            case "auth.open":
                using (var candidate = new SlipstreamClient(Text(p,"origin")))
                { Process.Start(new ProcessStartInfo(new Uri(candidate.Origin,"auth/cli").AbsoluteUri) { UseShellExecute = true }); }
                return new { opened = true };
            case "auth.exchange":
            {
                var candidate = new SlipstreamClient(Text(p,"origin"));
                try
                {
                    string credential = await candidate.ExchangeAsync(Text(p,"code"));
                    await candidate.ReadAsync("me");
                    if (!isolated) DeviceCredential.Save(candidate.Origin.GetLeftPart(UriPartial.Authority),credential);
                    client?.Dispose(); client = candidate; Store.State.Origin = candidate.Origin.GetLeftPart(UriPartial.Authority); Store.Save();
                    return await Discover();
                }
                catch { if (!ReferenceEquals(client,candidate)) candidate.Dispose(); throw; }
            }
            case "auth.logout":
                await Api.SignOutAsync(); if (!isolated) DeviceCredential.Delete(Store.State.Origin);
                client?.Dispose(); client = null; SharedSignInAvailable = false; accountUserId = null; Workspace = null; memberships.Clear(); approvals.Clear(); return await Bootstrap();
            case "navigation.get": return GetNavigation();
            case "navigation.save":
            {
                var preferences = JsonSerializer.Deserialize<NavigationPreferences>(p.GetRawText(),new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                if (preferences.CompanyGroups.Count>100 || preferences.HiddenTeams.Count>10000) throw new ArgumentException("Too many navigation entries.");
                var ids = new HashSet<string>(); var assigned = new HashSet<string>();
                foreach(var group in preferences.CompanyGroups)
                {
                    group.Name = group.Name.Trim();
                    if (group.Name.Length is < 1 or > 80 || group.Id.Length>64 || !ids.Add(group.Id)) throw new ArgumentException("Each company group needs a unique ID and a name of 1–80 characters.");
                    foreach(var key in group.Teams) if (!memberships.ContainsKey(key) || !assigned.Add(key)) throw new ServiceAccessException("A team must be authorized and belong to one company group.");
                }
                if (preferences.HiddenTeams.Any(key=>!memberships.ContainsKey(key))) throw new ServiceAccessException("Cannot change an unauthorized team's visibility.");
                Store.State.Navigation[AccountKey] = preferences; Store.Save(); NavigationChanged?.Invoke(AccountKey); return preferences;
            }
            case "workspace.select":
            {
                string key = Text(p,"key");
                await ValidateMembership(key);
                Workspace = memberships[key]; Store.State.SelectedWorkspace = key; Store.Save(); approvals.Clear();
                return Workspace;
            }
            case "workstream.draft.read":
            {
                var identity = Current(p);
                return new { text = WorkstreamDrafts.Read(directory,identity,Id(p)) };
            }
            case "workstream.draft.save":
            {
                var identity = Current(p);
                WorkstreamDrafts.Save(directory,identity,Id(p),Text(p,"text"));
                return new { saved = true };
            }
            case "projects":
            {
                string key = Text(p,"workspace"); await ValidateMembership(key);
                return await Api.ReadAsync("projects?navigation=1",memberships[key]);
            }
            case "project.tree":
            {
                string key = Text(p,"workspace"); await ValidateMembership(key);
                return await GetProject(memberships[key],Id(p));
            }
            case "project": return await GetProject(Current(p),Id(p));
            case "project.task":
            {
                var workspace = Current(p);
                return await Api.WriteAsync("projects/" + Id(p) + "/tasks",workspace,new { title = Text(p,"title"), description = Text(p,"description"), parent_task_id = OptionalId(p,"parent") });
            }
            case "visits": return Store.Visits(Current(p));
            case "dashboard": return await Api.ReadAsync("dashboard",Current(p));
            case "workstreams": return await Api.ReadAsync("workstreams",Current(p));
            case "workstream":
            {
                var workspace = Current(p); long id = Id(p);
                var data = await GetWorkstream(workspace,id);
                Store.Visit(new(workspace,"slipstream","workstream",id),Text(data.GetProperty("workstream"),"name"));
                return new { payload = data, visits = Store.Visits(workspace) };
            }
            case "workstream.direction":
            {
                var workspace = Current(p); await GetWorkstream(workspace,Id(p));
                return await Api.WriteAsync("workstreams/" + Id(p) + "/agent",workspace,new { prompt = Text(p,"prompt") });
            }
            case "workstream.pin":
            case "workstream.review":
            {
                var workspace = Current(p); var data = await GetWorkstream(workspace,Id(p));
                var visit = Store.Visit(new(workspace,"slipstream","workstream",Id(p)),Text(data.GetProperty("workstream"),"name"));
                if (method == "workstream.pin") visit.Pinned = p.GetProperty("pinned").GetBoolean();
                else visit.SeenEvent = data.GetProperty("workerEvents").EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).DefaultIfEmpty(visit.SeenEvent).Max();
                Store.Save(); return new { visits = Store.Visits(workspace) };
            }
            case "approval.prepare":
            {
                var workspace = Current(p); var data = await GetWorkstream(workspace,Id(p)); long taskId = p.GetProperty("task").GetInt64();
                var task = data.GetProperty("workstream").GetProperty("tasks").EnumerateArray().FirstOrDefault(item => item.GetProperty("id").GetInt64() == taskId);
                if (task.ValueKind == JsonValueKind.Undefined || Text(task,"status") != "proposed") throw new InvalidOperationException("This task is no longer proposed. Refresh its workstream.");
                string action = Text(p,"action"); if (action is not ("approve" or "reject")) throw new InvalidOperationException("Invalid decision.");
                if (action == "approve" && task.TryGetProperty("allow_elevated",out var grant) && grant.ValueKind == JsonValueKind.True) throw new InvalidOperationException("Review elevated task approvals in Slipstream.");
                string challenge = Guid.NewGuid().ToString("N"); approvals[challenge] = (workspace.Key,taskId,action,DateTime.UtcNow.AddMinutes(2));
                return new { challenge, task, action };
            }
            case "approval.commit":
            {
                var workspace = Current(p);
                if (!approvals.Remove(Text(p,"challenge"),out var approval) || approval.Workspace != workspace.Key || approval.Expires < DateTime.UtcNow) throw new InvalidOperationException("This decision expired. Review the task again.");
                return await Api.WriteAsync($"tasks/{approval.Task}/{approval.Action}",workspace,new { elevated = false });
            }
            case "machine":
                var sample = telemetry.Sample(); return new { name = Environment.MachineName, operatingSystem = Environment.OSVersion.ToString(), processors = Environment.ProcessorCount, sample.CpuPercent, sample.TotalMemory, sample.AvailableMemory, scope = "local" };
            case "terminal.create":
            {
                string id = Guid.NewGuid().ToString("N"); string shell = TerminalProfile.Validate(p.TryGetProperty("shell",out var requested) ? requested.GetString() ?? "wsl" : "wsl");
                var local = new LocalTerminal(id,ContextKey,shell);
                StartTerminal(local); terminals[id] = local; return TerminalInfo(local);
            }
            case "terminal.ready":
                var ready = Terminal(p); ready.Ready = true; ready.Session?.Resize(Columns(p),Rows(p)); return new { ready = true };
            case "terminal.input": await Terminal(p).Session!.WriteAsync(Text(p,"data")); return new { written = true };
            case "terminal.resize": Terminal(p).Session!.Resize(Columns(p),Rows(p)); return new { resized = true };
            case "terminal.ack": if (terminals.TryGetValue(Text(p,"id"),out var ack)) ack.InFlight = false; return new { acknowledged = true };
            case "terminal.restart":
                var restarting = Terminal(p); restarting.Session?.Dispose(); StartTerminal(restarting); return TerminalInfo(restarting);
            case "terminal.close":
                var closing = Terminal(p); terminals.Remove(closing.Id); closing.Dispose(); return new { closed = true };
            case "terminal.list": return terminals.Values.Where(t => t.Workspace == ContextKey).Select(TerminalInfo).ToArray();
            default: throw new InvalidOperationException("Unsupported Station action.");
        }
    }
    private NavigationPreferences GetNavigation()
    {
        if (!Store.State.Navigation.TryGetValue(AccountKey,out var preferences)) return new();
        return new NavigationPreferences
        {
            ShowNexusApp = preferences.ShowNexusApp,
            CompanyGroups = preferences.CompanyGroups.Select(group=>new CompanyGroup { Id=group.Id,Name=group.Name,Teams=group.Teams.Where(memberships.ContainsKey).ToList() }).ToList(),
            HiddenTeams = preferences.HiddenTeams.Where(memberships.ContainsKey).ToList(),
        };
    }
    private void OnNavigationChanged(string account)
    { if (client is not null && accountUserId is not null && AccountKey == account) Event?.Invoke(new { type="navigation.changed" }); }
    private async Task<JsonElement> GetProject(WorkspaceIdentity workspace,long id)
    {
        try { return await Api.ReadAsync("projects/" + id,workspace); }
        catch (ServiceRequestException error) when (error.StatusCode == 404)
        {
            // Older deployments expose project tasks through their existing
            // workstream API. Use that real contract until hierarchy is deployed;
            // do not invent parent links or advertise unsupported writes.
            var index = await Api.ReadAsync("projects",workspace);
            var project = index.GetProperty("projects").EnumerateArray().FirstOrDefault(item => item.GetProperty("id").GetInt64() == id);
            if (project.ValueKind == JsonValueKind.Undefined) throw;
            var lanes = project.TryGetProperty("workstreams",out var embedded) && embedded.ValueKind == JsonValueKind.Array
                ? embedded.EnumerateArray().ToArray()
                : (await Api.ReadAsync("workstreams",workspace)).GetProperty("workstreams").EnumerateArray().Where(item => item.TryGetProperty("project_id",out var projectId) && projectId.ValueKind == JsonValueKind.Number && projectId.GetInt64() == id).ToArray();
            var tasks = new Dictionary<long,JsonElement>();
            foreach (var lane in lanes)
            {
                var data = await GetWorkstream(workspace,lane.GetProperty("id").GetInt64());
                foreach (var task in data.GetProperty("workstream").GetProperty("tasks").EnumerateArray()) tasks[task.GetProperty("id").GetInt64()] = task.Clone();
            }
            return JsonSerializer.SerializeToElement(new { project,tasks = tasks.Values.ToArray(),workstreams = lanes,capabilities = new { task_hierarchy = false,task_creation = false },task_scope = "workstreams" });
        }
    }
    private async Task ValidateMembership(string key)
    {
        var me = await Api.ReadAsync("me");
        SharedSignInAvailable = AdvertisesSharedSignIn(me);
        if (!memberships.TryGetValue(key,out var identity) || me.GetProperty("user").GetProperty("id").GetInt64() != identity.UserId || !me.GetProperty("teams").EnumerateArray().Any(team => team.GetProperty("id").GetInt64() == identity.TeamId))
        { if (Workspace?.Key == key) Workspace = null; throw new ServiceAccessException("Organization access changed. Reconnect before continuing."); }
    }
    private WorkspaceIdentity Current(JsonElement p)
    {
        if (Workspace is null || Text(p,"workspace") != Workspace.Key) throw new InvalidOperationException("Select the originating organization before using this resource.");
        return Workspace;
    }
    private async Task<JsonElement> GetWorkstream(WorkspaceIdentity workspace,long id)
    {
        var data = await Api.ReadAsync("workstreams/" + id,workspace);
        var work = data.GetProperty("workstream");
        bool scope = work.TryGetProperty("team_id",out var team) && ((team.ValueKind == JsonValueKind.Number && team.GetInt64() == workspace.TeamId) || (team.ValueKind == JsonValueKind.Null && workspace.PersonalTeam));
        if (!scope) throw new ServiceAccessException("This workstream belongs to another organization."); return data;
    }
    private void StartTerminal(LocalTerminal terminal)
    {
        terminal.Generation++; int generation = terminal.Generation; terminal.InFlight = false; terminal.ExitCode = null;
        while (terminal.Output.TryDequeue(out _)) { } Interlocked.Exchange(ref terminal.Queued,0);
        terminal.Session = new PtySession(terminal.Shell,isolated,120,32,text =>
        {
            while (!terminal.Disposed && Volatile.Read(ref terminal.Generation) == generation && Volatile.Read(ref terminal.Queued) > 4000000) Thread.Sleep(5);
            if (terminal.Disposed || Volatile.Read(ref terminal.Generation) != generation) return;
            Interlocked.Add(ref terminal.Queued,text.Length); terminal.Output.Enqueue(text);
        },code => { if (terminal.Generation == generation) terminal.ExitCode = code; });
    }
    private void DrainTerminals()
    {
        foreach (var terminal in terminals.Values.ToArray())
        {
            if (!terminal.Ready || terminal.InFlight || terminal.Output.IsEmpty) continue;
            var output = new StringBuilder();
            while (output.Length < 65536 && terminal.Output.TryDequeue(out var text)) { Interlocked.Add(ref terminal.Queued,-text.Length); output.Append(text); }
            terminal.InFlight = true; Event?.Invoke(new { type = "terminal.output", id = terminal.Id, workspace = terminal.Workspace, data = output.ToString() });
        }
    }
    private LocalTerminal Terminal(JsonElement p)
    {
        if (!terminals.TryGetValue(Text(p,"id"),out var terminal) || terminal.Workspace != ContextKey) throw new InvalidOperationException("Terminal belongs to another organization or is closed."); return terminal;
    }
    private static object TerminalInfo(LocalTerminal t) => new { id = t.Id, workspace = t.Workspace, shell = t.Shell, displayName = t.Session?.DisplayName, processId = t.Session?.ProcessId, scope = "local", machine = Environment.MachineName, exitCode = t.ExitCode };
    private static int Columns(JsonElement p) => p.TryGetProperty("columns",out var value) ? Math.Clamp(value.GetInt32(),2,500) : 120;
    private static int Rows(JsonElement p) => p.TryGetProperty("rows",out var value) ? Math.Clamp(value.GetInt32(),2,200) : 32;
    private static long Id(JsonElement p) => p.GetProperty("id").GetInt64() is long id && id > 0 ? id : throw new ArgumentException("Invalid resource ID.");
    private static long? OptionalId(JsonElement p,string key) => p.TryGetProperty(key,out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
    private static string Text(JsonElement p,string key) => p.TryGetProperty(key,out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
    public void Dispose() { if (disposed) return; disposed = true; NavigationChanged -= OnNavigationChanged; terminalTimer.Stop(); foreach (var terminal in terminals.Values) terminal.Dispose(); terminals.Clear(); client?.Dispose(); }
    private sealed class LocalTerminal(string id,string workspace,string shell) : IDisposable
    {
        public string Id = id, Workspace = workspace, Shell = shell; public bool Ready, InFlight; public volatile bool Disposed;
        public int Generation, Queued; public int? ExitCode; public PtySession? Session; public ConcurrentQueue<string> Output = new();
        public void Dispose() { Disposed = true; Generation++; Session?.Dispose(); }
    }
}
