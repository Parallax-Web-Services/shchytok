using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Parallax.Station;

// User-chosen HTTPS navigation only. An activation cannot carry credentials,
// execute a command, change workspace permissions, or reach the native bridge.
internal static class StationLinks
{
    private static string PipeName => "Parallax.Station.Links." + WindowsIdentity.GetCurrent().User!.Value + "." + Process.GetCurrentProcess().SessionId;
    internal static string Create(Uri target) => "parallax-station://open?url=" + Uri.EscapeDataString(StationServices.ValidateWebAddress(target.AbsoluteUri).AbsoluteUri);
    internal static Uri Parse(string address)
    {
        if (address.Length>8192 || !Uri.TryCreate(address,UriKind.Absolute,out var uri) || uri.Scheme!="parallax-station" || uri.Host!="open" || uri.AbsolutePath is not ("" or "/") || uri.UserInfo!="" || !uri.IsDefaultPort || uri.Fragment!="" || !uri.Query.StartsWith("?url=",StringComparison.Ordinal) || uri.Query.Contains('&')) throw new ArgumentException("Use a Station link containing one HTTPS address.");
        return StationServices.ValidateWebAddress(Uri.UnescapeDataString(uri.Query[5..]));
    }
    internal static void Register()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Station executable is unavailable.");
        if (!executable.EndsWith("Parallax.Station.exe",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Register links from the installed Station executable.");
        using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes\parallax-station");
        root.SetValue("","URL:Parallax Station"); root.SetValue("URL Protocol","");
        using var icon = root.CreateSubKey("DefaultIcon"); icon.SetValue("",'"'+executable+'"'+",0");
        using var command = root.CreateSubKey(@"shell\open\command"); command.SetValue("",'"'+executable+"\" --open-url \"%1\"");
    }
    internal static async Task<bool> Forward(Uri target,string? channel = null)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".",channel ?? PipeName,PipeDirection.Out,PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            await pipe.ConnectAsync(timeout.Token); byte[] message = Encoding.UTF8.GetBytes(target.AbsoluteUri + "\n");
            await pipe.WriteAsync(message,timeout.Token); await pipe.FlushAsync(timeout.Token); return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException) { return false; }
    }
    internal static async Task Listen(Func<Uri,Task> receive,CancellationToken cancellation,string? channel = null)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(channel ?? PipeName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var bytes = new List<byte>(); var buffer = new byte[1];
                while (bytes.Count<=8192 && await pipe.ReadAsync(buffer,timeout.Token)==1 && buffer[0]!='\n') bytes.Add(buffer[0]);
                if (bytes.Count>8192) continue;
                var target = StationServices.ValidateWebAddress(new UTF8Encoding(false,true).GetString(bytes.ToArray()));
                await receive(target);
            }
            catch (OperationCanceledException) { if(cancellation.IsCancellationRequested)return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
            catch (Exception error) when (error is ArgumentException or DecoderFallbackException) { }
        }
    }
}
