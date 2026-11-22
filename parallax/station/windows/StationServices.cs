using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Parallax.Station;

public sealed record MachineSample(double? CpuPercent, ulong TotalMemory, ulong AvailableMemory);
public sealed class StationSettings
{
    public string WorkspaceUrl { get; set; } = "";
}
public static class StationServices
{
    public static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Parallax", "Station");
    private static readonly string SettingsPath = Path.Combine(StateDirectory, "settings.json");
    public static StationSettings LoadSettings(out string notice)
    {
        notice = "";
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<StationSettings>(File.ReadAllText(SettingsPath)) ?? new()
                : new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            notice = "Station could not read its settings. The existing file has been preserved.";
            return new();
        }
    }

    public static void SaveSettings(StationSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WorkspaceUrl)) ValidateWebAddress(settings.WorkspaceUrl);
        Directory.CreateDirectory(StateDirectory);
        string temporary = Path.Combine(StateDirectory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Uri ValidateWebAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Enter an HTTPS workspace address without embedded credentials.");
        return uri;
    }

    internal static bool IsAuthenticationAddress(Uri uri)
    {
        string path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/').ToLowerInvariant();
        var parts = path.Split('/');
        return parts.Any(part => part is "login" or "log-in" or "signin" or "sign-in" or "sign_in" or "signup" or "sign-up" or "register" or "authenticate" or "authentication" or "authorize" or "authorization")
            || path is "sso" or "auth/cli" or "auth/mobile";
    }
    public static void CheckLaunchInputs()
    {
        if (ValidateWebAddress("https://example.com/work?a=b").Scheme != "https") throw new InvalidOperationException("HTTPS validation failed.");
        foreach (string invalid in new[] { "javascript:alert(1)", "file:///C:/Windows/System32/cmd.exe", "http://example.com", "https://user:password@example.com", "not a URL" })
        {
            bool rejected = false;
            try { ValidateWebAddress(invalid); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Unsafe launch address was accepted.");
        }
    }
}

public sealed class MachineTelemetry
{
    private ulong? previousTotal, previousIdle;

    public MachineSample Sample()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory) || !GetSystemTimes(out var idle, out var kernel, out var user))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        ulong total = kernel.Value + user.Value;
        double? cpu = previousTotal is ulong before && total > before && previousIdle is ulong idleBefore
            ? Math.Clamp(100.0 * (1.0 - (double)(idle.Value - idleBefore) / (total - before)), 0, 100) : null;
        previousTotal = total;
        previousIdle = idle.Value;
        return new(cpu, memory.TotalPhysical, memory.AvailablePhysical);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
