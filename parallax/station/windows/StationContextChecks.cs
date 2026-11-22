using System.IO;
using System.Text.Json;

namespace Parallax.Station;

internal static class StationContextChecks
{
    public static void VerifyRestart(string output)
    {
        var first = new WorkspaceIdentity("https://station-contract.invalid",41,11,"Test organization A");
        var store = new WorkspaceStore(Path.Combine(output,"context-contract"));
        var visit = store.Visits(first).Single();
        if (store.State.SelectedWorkspace != first.Key || !visit.Pinned || visit.Id != 73 || visit.SeenEvent != 108)
            throw new InvalidOperationException("Workstream context did not survive process restart.");
        File.WriteAllText(Path.Combine(output,"context-restart.json"), JsonSerializer.Serialize(new { verified = true, processId = Environment.ProcessId, workspaceKey = first.Key, workstreamId = visit.Id, pinned = visit.Pinned, seenEvent = visit.SeenEvent }));
    }
}
