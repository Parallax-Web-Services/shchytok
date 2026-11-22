using System.IO;

namespace Parallax.Station;

internal sealed record TerminalProfile(string Executable,string Arguments,string DisplayName)
{
    internal static string Validate(string shell) => shell is "wsl" or "cmd" or "powershell" ? shell : throw new ArgumentException("Choose Ubuntu (WSL), PowerShell or Command Prompt.");

    internal static TerminalProfile Resolve(string shell,bool isolatedTest)
    {
        Validate(shell);
        if (shell == "wsl") return Existing(Path.Combine(Environment.SystemDirectory,"wsl.exe"),isolatedTest ? " --cd ~ --exec env HISTFILE=/dev/null bash --noprofile --norc -i" : " --cd ~","Ubuntu","WSL is unavailable. Choose PowerShell or Command Prompt.");
        if (shell == "cmd") return Existing(Path.Combine(Environment.SystemDirectory,"cmd.exe")," /D","Command Prompt","Command Prompt is unavailable.");

        // Prefer the installed stable PowerShell 7, with no PATH lookup or install.
        string programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string modern = Path.Combine(programFiles,"PowerShell","7","pwsh.exe");
        string executable = File.Exists(modern) ? modern : Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");
        string arguments = " -NoLogo";
        if (isolatedTest)
        {
            // Isolated checks must not load a user profile or save their commands
            // to the user's PSReadLine history. Normal interactive sessions retain
            // the user's own PowerShell profile and preferences.
            arguments += " -NoProfile -NoExit -Command \"if (Get-Module -ListAvailable PSReadLine) { Import-Module PSReadLine; Set-PSReadLineOption -HistorySaveStyle SaveNothing }\"";
        }
        return Existing(executable,arguments,File.Exists(modern) ? "PowerShell 7" : "Windows PowerShell","PowerShell is unavailable on this Windows device.");
    }

    private static TerminalProfile Existing(string executable,string arguments,string name,string error) => File.Exists(executable) ? new(executable,arguments,name) : throw new IOException(error);
}
