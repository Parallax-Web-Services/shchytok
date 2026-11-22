using System.Diagnostics;
using System.Text.Json;

namespace Parallax.Station;

internal sealed record BrowserPreferences(string LinkMode);
internal static class BrowserSettings
{
    internal static string ValidateMode(string? mode) => mode is "station" or "external" ? mode : throw new ArgumentException("Choose Station or your default browser.");
    internal static BrowserPreferences Read(StationRuntime runtime) => new(runtime.Store.State.BrowserLinkMode);
    internal static BrowserPreferences Save(StationRuntime runtime,JsonElement p)
    {
        runtime.Store.State.BrowserLinkMode = ValidateMode(p.GetProperty("linkMode").GetString()); runtime.Store.Save(); return Read(runtime);
    }
    internal static void OpenSystemSettings() => Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
}
