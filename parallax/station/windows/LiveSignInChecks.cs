using System.IO;
using System.Text.Json;

namespace Parallax.Station;

// Explicit native diagnostic: use the current device credential with a fresh browser
// profile. Never exports credentials, page contents or authorization URL queries.
internal static class LiveSignInChecks
{
    internal static async Task Run(string output)
    {
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output,"profile","fixture"));
        using var runtime=new StationRuntime(null);
        await runtime.Bootstrap();
        if(!runtime.SharedSignInAvailable)throw new InvalidOperationException("Saved connection does not advertise shared sign-in.");
        var window=new MainWindow(Path.Combine(output,"profile"),runtime);
        var host=window.WebHost;
        host.LiveSignInCheck=true;
        host.UseSavedBrowserProfileForCheck=true;
        host.SignInTrace=value=>File.AppendAllText(Path.Combine(output,"navigation.txt"),DateTime.UtcNow.ToString("O")+" "+value+Environment.NewLine);
        window.Show();
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(30));
        for(int i=0;i<300 && await host.Browser.ExecuteScriptAsync("window.station.bootstrapped===true")!="true";i++)await Task.Delay(100);
        var results=new List<object>();
        foreach(var address in new[]{"https://continuum.ceo/quark","https://continuum.ceo/dashboard"})
        {
            host.SignInTrace("Opening " + address);
            string name=address.EndsWith("/quark")?"Open Quark":"Open Continuum";
            int count=host.BrowserPageCount;
            await host.Browser.ExecuteScriptAsync("document.querySelector("+JsonSerializer.Serialize(".sidebar-apps button[aria-label='"+name+"']")+").click()");
            for(int i=0;i<700;i++)
            {
                if(host.BrowserPageCount>count && host.IsPageVisible(host.BrowserPageIds.Last()))break;
                if(await host.Browser.ExecuteScriptAsync("!!document.querySelector('.station-notice')")=="true")break;
                await Task.Delay(100);
            }
            string notice=await host.Browser.ExecuteScriptAsync("document.querySelector('.station-notice')?.textContent||null");
            host.SignInTrace("Shell notice: "+notice);
            string id=host.BrowserPageIds.Last();
            var core=host.PageCore(id);
            await Task.Delay(1000);
            string metrics=await core.ExecuteScriptAsync("({ready:document.readyState,hidden:document.hidden,visibility:document.visibilityState,width:innerWidth,height:innerHeight,dpr:devicePixelRatio,bodyText:document.body?.innerText?.length,bodyChildren:document.body?.childElementCount,appChildren:document.querySelector('#app')?.childElementCount,rootChildren:document.querySelector('#root')?.childElementCount,bodyBackground:getComputedStyle(document.body).backgroundColor,htmlBackground:getComputedStyle(document.documentElement).backgroundColor,scripts:[...document.scripts].map(s=>{try{return new URL(s.src).pathname}catch{return 'inline'}})})");
            File.WriteAllText(Path.Combine(output,address.EndsWith("/quark")?"quark-metrics.json":"continuum-metrics.json"),metrics);
            using(var capture=File.Create(Path.Combine(output,address.EndsWith("/quark")?"quark-webview.png":"continuum-webview.png")))await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,capture);
            var density=System.Windows.Media.VisualTreeHelper.GetDpi(host);
            var painted=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(host.ActualWidth*density.DpiScaleX),(int)Math.Round(host.ActualHeight*density.DpiScaleY),96*density.DpiScaleX,96*density.DpiScaleY,System.Windows.Media.PixelFormats.Pbgra32);
            painted.Render(host);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(painted));
            using(var capture=File.Create(Path.Combine(output,address.EndsWith("/quark")?"quark-composited.png":"continuum-composited.png")))encoder.Save(capture);
            var landed=new Uri(core.Source);
            bool login=StationServices.IsAuthenticationAddress(landed);
            results.Add(new{requested=address,landed=landed.GetLeftPart(UriPartial.Path),login,externalHandoffs=host.ExternalNavigations.Count,visible=host.IsPageVisible(id),notice});
            File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            if(login || host.ExternalNavigations.Count>0 || landed.Host!="continuum.ceo" || !host.IsPageVisible(id) || notice!="null")throw new InvalidOperationException("Application did not complete shared sign-in. See credential-free navigation trace.");
        }
        string failure=Path.Combine(output,"failure.txt");
        if(File.Exists(failure))File.Move(failure,Path.Combine(output,"previous-failure-"+DateTime.UtcNow.Ticks+".txt"));
    }
}
