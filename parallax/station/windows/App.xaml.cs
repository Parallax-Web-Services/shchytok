using System.IO;
using System.Windows;
using System.Text.Json;

namespace Parallax.Station;

public partial class App : Application
{
    private readonly CancellationTokenSource activation = new();
    protected override void OnExit(ExitEventArgs e) { activation.Cancel(); base.OnExit(e); }
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--check-shared-sign-in")
        {
            try { await LiveSignInChecks.Run(e.Args[1]); Shutdown(0); }
            catch(Exception error) { Directory.CreateDirectory(e.Args[1]); File.WriteAllText(Path.Combine(e.Args[1],"failure.txt"),error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--verify-context")
        {
            try { StationContextChecks.VerifyRestart(e.Args[1]); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(e.Args[1],"context-restart-failure.txt"),error.ToString()); Shutdown(1); }
            return;
        }
        string? output = e.Args.Length == 2 && e.Args[0] == "--smoke-test" ? Path.GetFullPath(e.Args[1]) : null;
        try
        {
            if (output is not null)
            {
                Directory.CreateDirectory(Path.Combine(output,"fixture"));
                File.WriteAllText(Path.Combine(output,"fixture","station-proof.txt"),"An isolated terminal working-directory fixture.");
            }
            Uri? target = e.Args.Length==2 && e.Args[0]=="--open-url" ? StationLinks.Parse(e.Args[1]) : null;
            if(target is not null && await StationLinks.Forward(target)) { Shutdown(0); return; }
            var window = new MainWindow(output); MainWindow = window; window.Show();
            if (output is null)
            {
                async Task Open(Uri uri)
                {
                    var selected=Windows.OfType<MainWindow>().FirstOrDefault(w=>w.IsActive) ?? Windows.OfType<MainWindow>().LastOrDefault() ?? window;
                    if(selected.WindowState==WindowState.Minimized)selected.WindowState=WindowState.Normal;
                    selected.Activate();
                    await selected.WebHost.Ready;
                    for(int attempt=0;attempt<400;attempt++)
                    {
                        if(await selected.WebHost.Browser.ExecuteScriptAsync("window.station.bootstrapped===true")=="true")break;
                        await Task.Delay(100);
                    }
                    selected.Activate();
                    await selected.WebHost.Browser.ExecuteScriptAsync("window.station.openBrowser("+JsonSerializer.Serialize(uri.AbsoluteUri)+",'Browser',{forceInside:true})");
                }
                _ = StationLinks.Listen(uri=>Dispatcher.InvokeAsync(()=>Open(uri)).Task.Unwrap(),activation.Token);
                if(target is not null)await Open(target);
                return;
            }
            await WebShellChecks.Run(window,output); Shutdown(0);
        }
        catch (Exception error)
        {
            if (output is not null) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString()); }
            else MessageBox.Show(error.Message,"Parallax Station",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
