using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace Parallax.Station;

internal static class WebShellChecks
{
    public static async Task Run(MainWindow window,string output)
    {
        var host = window.WebHost!;
        foreach (var unavailable in new[] { "{}", "{\"station_sign_in\":{\"available\":false,\"nexus_origin\":\"https://my.parallaxweb.services\"}}", "{\"station_sign_in\":{\"available\":true,\"nexus_origin\":\"https://untrusted.example\"}}" })
            if (StationRuntime.AdvertisesSharedSignIn(JsonSerializer.Deserialize<JsonElement>(unavailable))) throw new InvalidOperationException("Shared sign-in attempted without an advertised trusted capability.");
        if (!StationRuntime.AdvertisesSharedSignIn(JsonSerializer.Deserialize<JsonElement>("{\"station_sign_in\":{\"available\":true,\"nexus_origin\":\"https://my.parallaxweb.services\"}}"))) throw new InvalidOperationException("Advertised shared sign-in was ignored.");
        bool contractFixture = File.Exists(Path.Combine(output,"project-hierarchy-contract.json"));
        if (contractFixture)
        {
            var api = new SlipstreamClient("https://station-contract.invalid",new FixtureApi(output,startupFailures:2)); api.Authenticate("synthetic-ui-contract-token"); host.Runtime.SetTestClient(api);
        }
        if (contractFixture)
        {
            using var compatibility = new StationRuntime(Path.Combine(output,"legacy-project-contract"));
            var legacy = new SlipstreamClient("https://station-contract.invalid",new FixtureApi(output,legacyProject:true)); legacy.Authenticate("synthetic-legacy-contract-token"); compatibility.SetTestClient(legacy);
            await compatibility.Bootstrap();
            var result = (JsonElement)await compatibility.Call("project.tree",JsonSerializer.SerializeToElement(new { workspace = compatibility.Workspace!.Key,id = 1 }));
            if (result.GetProperty("tasks").GetArrayLength() != 1 || result.GetProperty("capabilities").GetProperty("task_creation").GetBoolean()) throw new InvalidOperationException("Existing service workstream tasks were not exposed as read-only project navigation.");
            bool rejected = false;
            try { await compatibility.Call("project.tree",JsonSerializer.SerializeToElement(new { workspace = "unauthorized-organization",id = 1 })); }
            catch (ServiceAccessException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Project navigation accepted an unauthorized organization.");
        }
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(35));
        await BrowserContractChecks.Run();
        if(host.Browser.CoreWebView2.Settings.IsStatusBarEnabled)throw new InvalidOperationException("The shell still exposes the browser link-status footer.");
        async Task<string> Script(string source) => await host.Browser.ExecuteScriptAsync(source);
        async Task<JsonElement> Rpc(string method,object? parameters = null)
        {
            string key = Guid.NewGuid().ToString("N");
            await Script($"window.__check=window.__check||{{}};window.station.call({JsonSerializer.Serialize(method)},{JsonSerializer.Serialize(parameters ?? new { })}).then(data=>window.__check[{JsonSerializer.Serialize(key)}]={{ok:true,data}},error=>window.__check[{JsonSerializer.Serialize(key)}]={{ok:false,error:error.message}})");
            for (int attempt = 0; attempt < 300; attempt++)
            {
                string result = await Script($"window.__check[{JsonSerializer.Serialize(key)}]||null");
                if (result != "null")
                {
                    using var parsed = JsonDocument.Parse(result); var value = parsed.RootElement;
                    if (!value.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(value.GetProperty("error").GetString());
                    return value.GetProperty("data").Clone();
                }
                await Task.Delay(100);
            }
            throw new TimeoutException("Shell RPC timed out: " + method);
        }
        async Task Wait(string expression,string failure)
        {
            for (int attempt = 0; attempt < 250; attempt++) { if (await Script(expression) == "true") return; await Task.Delay(100); }
            File.WriteAllText(Path.Combine(output,"shell-failure-state.json"),await Script("JSON.stringify({tree:document.querySelector('.project-tree')?.innerText,buttons:[...document.querySelectorAll('.project-tree button')].map(button=>({label:button.getAttribute('aria-label'),text:button.textContent,svg:button.querySelector('svg')?.getAttribute('class')})),notice:document.querySelector('.station-notice')?.innerText,expanded:localStorage.getItem('station.expanded')})"));
            using (var capture = File.Create(Path.Combine(output,"shell-failure.png"))) await host.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,capture);
            throw new InvalidOperationException(failure);
        }
        async Task Click(string selector,string text)
        {
            await Wait($"[...document.querySelectorAll({JsonSerializer.Serialize(selector)})].some(button=>button.textContent.trim()==={JsonSerializer.Serialize(text)})","Missing web control: " + text);
            await Script($"[...document.querySelectorAll({JsonSerializer.Serialize(selector)})].find(button=>button.textContent.trim()==={JsonSerializer.Serialize(text)}).click()");
        }
        async Task Capture(string name)
        { await Task.Delay(250); using var file = File.Create(Path.Combine(output,name + ".png")); await host.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file); }
        await Wait("!!document.querySelector('.station-brand')","React Station shell did not mount.");
        if(contractFixture)
        {
            await Wait("!!document.querySelector('.connection-status')","An unavailable startup connection was not handled as offline.");
            await Click(".connection-status button","Retry connection");
            await Wait("!document.querySelector('.connection-status') && document.querySelector('.station-statusbar').textContent.includes('Connected workspace')","Startup recovery did not reuse the saved connection.");
        }
        await Wait("document.querySelectorAll('.sidebar-apps nav button').length===7 && [...document.querySelectorAll('.sidebar-apps img')].every(image => image.naturalWidth>0)","Applications were not directly accessible from the Station sidebar.");
        await Script("document.querySelector('.station-brand-launcher .parallax-launcher-trigger').click()");
        await Wait("document.querySelectorAll('[role=dialog] .parallax-launcher-card').length===7","The shared web app switcher did not open from the sidebar header.");
        if(contractFixture)
        {
            await host.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText","{\"text\":\"Slipstream\"}");
            await Wait("document.querySelectorAll('[role=dialog] .parallax-launcher-card').length===1","Header switcher search did not filter applications.");
            await Script("document.querySelector('[role=dialog] [data-product=slipstream] a').click()");
            await Wait("!document.querySelector('[role=dialog]') && !!document.querySelector('.tab-pane.active .slipstream-workspace')","Choosing a header switcher app did not close the dialog and open its Station surface.");
            await Click(".primary-nav button","HomeCtrl K");
        }
        else
        {
            await Script("document.querySelector('[role=dialog] .parallax-launcher-close').click()");
            await Wait("!document.querySelector('[role=dialog]')","The header switcher did not close.");
        }
        await Capture("shell-home");
        await Rpc("browser.preferences.save",new { linkMode="external" });
        int modePages=host.BrowserPageCount;
        var externalPreference=await Rpc("browser.open",new { url="https://station-check.invalid/station-test.html" });
        if(!externalPreference.GetProperty("external").GetBoolean() || host.BrowserPageCount!=modePages)throw new InvalidOperationException("The default-browser preference was ignored.");
        await Rpc("browser.preferences.save",new { linkMode="station" });
        await Click(".primary-nav button","Apps");
        await Wait("document.querySelectorAll('.tab-pane.active .parallax-launcher-card').length===7","The persistent Core app grid did not render.");
        await Wait("!document.querySelector('[data-product=nexus]')","Nexus appeared without opting into SaaS administration.");
        await Capture("shell-apps");
        if(contractFixture)
        {
            await Click(".sidebar-apps button","SLIPSTREAM");
            await Wait("!!document.querySelector('.tab-pane.active .slipstream-workspace')","Slipstream opened a website instead of its connected operational surface.");
            await Wait("!!document.querySelector('.slipstream-workspace [aria-label=\"SLIPSTREAM workstreams\"] .work-card')","Slipstream did not load the existing workstream connection.");
            if(host.BrowserPageCount!=0)throw new InvalidOperationException("Opening connected Slipstream required a separate browser login.");
            await Click(".primary-nav button","Apps");
        }
        await Wait("document.querySelector('[data-product=unravel] a')?.href==='https://unravel.tools/dashboard' && document.querySelector('[data-product=unravel] img')?.naturalWidth>0","Unravel app entry or its existing logo was not packaged.");
        if (contractFixture)
        {
            await Click(".sidebar-tools button","Settings");
            await Script("document.querySelector('[aria-label=\"Show NEXUS for SaaS administration\"]').click()");
            await Wait("document.querySelector('[data-product=nexus]')!==null","Opting into SaaS administration did not expose Nexus.");
            await Script("document.querySelector('[aria-label=\"Show NEXUS for SaaS administration\"]').click()");
            await Wait("document.querySelector('[data-product=nexus]')===null","Turning off optional Nexus did not hide it.");
            await Click(".primary-nav button","Apps");
        }
        await Script("document.querySelector('.tab-pane.active .parallax-launcher-search input').focus()");
        await host.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText","{\"text\":\"Quark\"}");
        await Wait("document.querySelectorAll('.tab-pane.active .parallax-launcher-card').length===1","App grid search failed.");
        await Click(".primary-nav button","HomeCtrl K");
        await Click(".primary-nav button","Apps");
        await Wait("document.querySelector('.tab-pane.active .parallax-launcher-search input').value==='Quark'","Returning to Apps lost its state.");
        await Click(".sidebar-tools button","TerminalLocal");
        await Wait("!!document.querySelector('.terminal-view.active .xterm')","Web terminal did not open.");
        await Wait("document.querySelector('.tab-pane.active .terminal-session-tabs img')?.naturalWidth>0 && getComputedStyle(document.querySelector('.tab-pane.active .terminal-sessions')).backgroundColor==='rgb(48, 10, 36)'","Ubuntu terminal logo or color scheme did not render.");
        var sessions = await Rpc("terminal.list");
        if (sessions.GetArrayLength() != 1 || sessions[0].GetProperty("shell").GetString() != "wsl") throw new InvalidOperationException("Terminal did not default to one WSL session.");
        string first = sessions[0].GetProperty("id").GetString()!; int process = sessions[0].GetProperty("processId").GetInt32();
        async Task Input(string text,string? id = null) => await Rpc("terminal.input",new { id = id ?? first,data = text });
        async Task Screen(string needle,string? id = null) => await Wait($"window.station.readTerminal({JsonSerializer.Serialize(id ?? first)}).includes({JsonSerializer.Serialize(needle)})","Terminal did not display: " + needle);
        await Input("printf 'WEB_KERNEL=%s\\n' \"$(uname -s)\"; test -t 0 && printf 'WEB_TTY_%s\\n' OK\r");
        await Screen("WEB_KERNEL=Linux"); await Screen("WEB_TTY_OK");
        await Input("read -rs -p \"$(printf WEB_%s_READY SECRET)\" STATION_SECRET; printf '\\nWEB_SECRET_%s\\n' DONE; unset STATION_SECRET\r");
        await Screen("WEB_SECRET_READY"); await Input("web-synthetic-secret-42\r"); await Screen("WEB_SECRET_DONE");
        if (await Script($"window.station.readTerminal('{first}').includes('web-synthetic-secret-42')") != "false") throw new InvalidOperationException("The no-echo secret was displayed.");
        await Input("sleep 30\r"); await Task.Delay(200); await Input("\u0003"); await Input("printf 'WEB_INTERRUPT_%s\\n' OK\r"); await Screen("WEB_INTERRUPT_OK");
        string fixture = Path.Combine(output,"fixture").Replace("'","'\\''");
        await Input("cd -- \"$(wslpath -u '" + fixture + "')\"\r");
        await Click(".terminal-toolbar button","New terminal");
        await Wait("document.querySelectorAll('.terminal-session-tabs>div:not(.terminal-actions)').length===2","Second terminal session was not presented in React.");
        sessions = await Rpc("terminal.list"); string second = sessions[1].GetProperty("id").GetString()!;
        await Input("printf 'WEB_SECOND_%s\\n' OK\r",second); await Screen("WEB_SECOND_OK",second);
        await Input("test -f station-proof.txt && printf 'WEB_CWD_%s\\n' PRESERVED\r"); await Screen("WEB_CWD_PRESERVED");
        if (sessions[1].GetProperty("processId").GetInt32() == process) throw new InvalidOperationException("Terminal sessions share one process.");
        await Script("document.querySelector('[aria-label=\"Close terminal 2\"]').parentElement.dispatchEvent(new MouseEvent('auxclick',{button:1,bubbles:true,cancelable:true}))");
        await Wait("document.querySelectorAll('.terminal-session-tabs>div:not(.terminal-actions)').length===1","Closing a terminal did not update React.");
        await Script("document.querySelector('[role=switch][aria-label=\"Use WSL\"]').click()");
        await Wait("document.querySelector('[role=switch][aria-label=\"Use WSL\"]').getAttribute('aria-checked')==='false' && document.querySelectorAll('.terminal-session-tabs>div').length===2","WSL toggle did not open or select a Windows session.");
        var toggled = await Rpc("terminal.list"); string windowsSession = toggled.EnumerateArray().Single(session => session.GetProperty("shell").GetString() == "cmd").GetProperty("id").GetString()!;
        await Input("echo WEB_CMD_%OS%\r",windowsSession); await Screen("WEB_CMD_Windows_NT",windowsSession);
        await Script("document.querySelector('[role=switch][aria-label=\"Use WSL\"]').click()");
        await Wait("document.querySelector('[role=switch][aria-label=\"Use WSL\"]').getAttribute('aria-checked')==='true'","WSL toggle did not return to the existing WSL session.");
        if ((await Rpc("terminal.list")).GetArrayLength() != 2) throw new InvalidOperationException("Toggling WSL restarted or duplicated an existing session.");
        await Script("document.querySelector('[aria-label=\"Close terminal 2\"]').click()");
        await Wait("document.querySelectorAll('.terminal-session-tabs>div').length===1","Closing the Windows profile session failed.");
        await Capture("shell-terminal");
        var sizeJson = await Script($"window.station.terminalSize('{first}')"); using var sizeDoc = JsonDocument.Parse(sizeJson);
        string dimensions = sizeDoc.RootElement.GetProperty("rows").GetInt32() + " " + sizeDoc.RootElement.GetProperty("columns").GetInt32();
        await Input("printf 'WEB_SIZE=%s\\n' \"$(stty size)\"\r"); await Screen("WEB_SIZE=" + dimensions);
        await Script("(()=>{const select=document.querySelector('[aria-label=\"Terminal shell profile\"]');select.value='powershell';select.dispatchEvent(new Event('change',{bubbles:true}));})()");
        await Wait("document.querySelector('[aria-label=\"Terminal shell profile\"]').value==='powershell' && document.querySelectorAll('.terminal-session-tabs>div').length===2","Selecting PowerShell did not create a separate terminal session.");
        var powerShellInfo = (await Rpc("terminal.list")).EnumerateArray().Single(session => session.GetProperty("shell").GetString() == "powershell");
        string powerShellSession = powerShellInfo.GetProperty("id").GetString()!;
        int powerShellProcess = powerShellInfo.GetProperty("processId").GetInt32();
        string powerShellName = powerShellInfo.GetProperty("displayName").GetString()!;
        using (var nativePowerShell = Process.GetProcessById(powerShellProcess))
            if (nativePowerShell.ProcessName != (powerShellName == "PowerShell 7" ? "pwsh" : "powershell")) throw new InvalidOperationException("PowerShell did not launch the selected native executable.");
        await Input("$StationProof = 41; Write-Output ('WEB_POWERSHELL_' + $PSVersionTable.PSVersion.Major); Write-Output ('WEB_PS_STATE_' + ($StationProof + 1)); Write-Output ('WEB_PS_HISTORY_' + (Get-PSReadLineOption).HistorySaveStyle)\r",powerShellSession);
        await Screen(powerShellName == "PowerShell 7" ? "WEB_POWERSHELL_7" : "WEB_POWERSHELL_5",powerShellSession);
        await Screen("WEB_PS_STATE_42",powerShellSession); await Screen("WEB_PS_HISTORY_SaveNothing",powerShellSession);
        await Script("document.querySelector('[role=switch][aria-label=\"Use WSL\"]').click()");
        await Wait("document.querySelector('[aria-label=\"Terminal shell profile\"]').value==='wsl'","WSL toggle did not return from PowerShell to the existing Ubuntu session.");
        await Script("document.querySelector('[role=switch][aria-label=\"Use WSL\"]').click()");
        await Wait("document.querySelector('[aria-label=\"Terminal shell profile\"]').value==='powershell'","WSL toggle did not remember the selected Windows PowerShell profile.");
        if ((await Rpc("terminal.list")).GetArrayLength()!=2) throw new InvalidOperationException("Switching between WSL and PowerShell duplicated sessions.");
        await Capture("shell-terminal-powershell");
        if (contractFixture)
        {
            await Click(".primary-nav button","HomeCtrl K");
            await Wait("[...document.querySelectorAll('.tree-label')].some(button=>button.textContent==='First project')","Authorized projects did not appear in the React sidebar.");
            await Script("{const button=document.querySelector('[aria-label=\"Expand First project\"]');if(!button.querySelector('.rotated'))button.click();}");
            await Wait("!![...document.querySelectorAll('.tree-expander')].find(button=>button.getAttribute('aria-label')?.endsWith('Parent task'))","Parent task did not load.");
            if (await Script("!!document.querySelector('.tab-pane.active .home-intro')") != "true") throw new InvalidOperationException("Expanding projects unexpectedly opened a details tab.");
            await Script("{const button=[...document.querySelectorAll('.tree-expander')].find(button=>button.getAttribute('aria-label')?.endsWith('Parent task'));if(!button.querySelector('.rotated'))button.click();}");
            await Wait("!![...document.querySelectorAll('.tree-expander')].find(button=>button.getAttribute('aria-label')?.endsWith('Child task'))","Child task did not nest.");
            await Script("{const button=[...document.querySelectorAll('.tree-expander')].find(button=>button.getAttribute('aria-label')?.endsWith('Child task'));if(!button.querySelector('.rotated'))button.click();}");
            await Click(".tree-label","Nested subtask");
            await Wait("document.querySelector('.tab-pane.active h1')?.textContent==='Nested subtask'","Nested task selection did not open its details.");
            await Capture("shell-project");
            await Click(".primary-nav button","HomeCtrl K");
            await Wait("!!document.querySelector('.tab-pane.active .work-card')","Service workstreams did not appear on Home.");
            await Script("document.querySelector('.tab-pane.active .work-card').click()");
            await Wait("document.querySelector('.tab-pane.active .messages')?.textContent.includes('Inspect the project; wait for human approval.')===true","Agent conversation did not render from the service contract.");
            await Capture("shell-workstream");
            await Click(".tab-pane.active .decision-buttons button","Review approval");
            await Wait("!!document.querySelector('[role=dialog]')","Human decision did not open in React.");
            await Click("[role=dialog] button","Cancel");
        }
        await Click(".sidebar-tools button","Infrastructure");
        await Wait("!!document.querySelector('.tab-pane.active progress')","Native telemetry did not render in React.");
        await Wait("document.querySelectorAll('.tab-pane.active [data-infrastructure]').length===2 && !document.querySelector('.sidebar-apps [aria-label=\"Open NEBULA\"]') && !document.querySelector('.sidebar-apps [aria-label=\"Open NUCLEUS\"]')","Infrastructure services were not grouped with hardware.");
        await Capture("shell-machine");
        File.Copy(Path.Combine(AppContext.BaseDirectory,"Assets","Shell","assets","quark-102.png"),Path.Combine(output,"fixture","favicon.png"),true);
        File.WriteAllText(Path.Combine(output,"fixture","station-test.html"),"<!doctype html><title>Station isolated browser proof</title><link rel=icon type=image/png href=/favicon.png><h1>Separate app surface</h1>");
        int environmentAttempts=0;
        host.TestEnvironmentFactory=profile=>++environmentAttempts==1
            ? Task.FromCanceled<CoreWebView2Environment>(new CancellationToken(true))
            : CoreWebView2Environment.CreateAsync(null,profile);
        await Script("window.station.openBrowser('https://station-check.invalid/station-test.html','Browser proof')");
        await Wait("!!document.querySelector('.tab-pane.active .browser-region')","Browser did not open in a React tab.");
        string browserId = host.BrowserPageIds.Single();
        if(host.PageCore(browserId).Settings.IsStatusBarEnabled)throw new InvalidOperationException("An embedded app still exposes the browser link-status footer.");
        if(environmentAttempts!=2)throw new InvalidOperationException("Canceled app environment initialization was cached or not retried exactly once.");
        host.TestEnvironmentFactory=null;
        await Task.Delay(700);
        using(var overlapTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            string target="https://station-check.invalid/station-test.html?overlap-proof=1";
            var appCore=host.PageCore(browserId);
            if(!await StationWebHost.NavigateAndWait(appCore,target,()=>{appCore.Navigate("about:blank");appCore.Navigate(target);},overlapTimeout.Token) || appCore.Source!=target)
                throw new InvalidOperationException("An obsolete navigation completion canceled the requested app navigation.");
        }
        await Task.Delay(500);
        var density=System.Windows.Media.VisualTreeHelper.GetDpi(host);
        var composite=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(host.ActualWidth*density.DpiScaleX),(int)Math.Round(host.ActualHeight*density.DpiScaleY),96*density.DpiScaleX,96*density.DpiScaleY,System.Windows.Media.PixelFormats.Pbgra32);
        composite.Render(host);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(composite));
        using(var painted=File.Create(Path.Combine(output,"shell-browser-composited.png")))encoder.Save(painted);
        var region=host.PageBounds(browserId);var pixel=new byte[4];
        var origin=host.PointToScreen(region.TopLeft);
        if(Math.Abs(origin.X-Math.Round(origin.X))>0.01 || Math.Abs(origin.Y-Math.Round(origin.Y))>0.01 || Math.Abs(region.Width*density.DpiScaleX-Math.Round(region.Width*density.DpiScaleX))>0.01 || Math.Abs(region.Height*density.DpiScaleY-Math.Round(region.Height*density.DpiScaleY))>0.01)
            throw new InvalidOperationException("The browser surface requires fractional-pixel image scaling.");
        composite.CopyPixels(new System.Windows.Int32Rect((int)((region.X+region.Width/2)*density.DpiScaleX),(int)((region.Y+region.Height/2)*density.DpiScaleY),1,1),pixel,4,0);
        if(pixel[0]<230 || pixel[1]<230 || pixel[2]<230 || pixel[3]<230)throw new InvalidOperationException("The loaded app pixels were covered by the shell browser surface.");
        using(var rendered=new MemoryStream())
        {
            await host.PageCore(browserId).CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,rendered);rendered.Position=0;
            var image=System.Windows.Media.Imaging.BitmapDecoder.Create(rendered,System.Windows.Media.Imaging.BitmapCreateOptions.None,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
            string pageDensity=await host.PageCore(browserId).ExecuteScriptAsync("({width:innerWidth,height:innerHeight,dpr:devicePixelRatio})");
            File.WriteAllText(Path.Combine(output,"browser-pixel-density.json"),JsonSerializer.Serialize(new { captureWidth=image.PixelWidth,captureHeight=image.PixelHeight,surfaceWidth=region.Width,surfaceHeight=region.Height,dpiX=density.DpiScaleX,dpiY=density.DpiScaleY,pageDensity,
                frame=host.PageFrameMetrics(browserId) },new JsonSerializerOptions{WriteIndented=true}));
            if(Math.Abs(image.PixelWidth-Math.Round(region.Width*density.DpiScaleX))>1 || Math.Abs(image.PixelHeight-Math.Round(region.Height*density.DpiScaleY))>1)
                throw new InvalidOperationException($"Browser capture {image.PixelWidth}x{image.PixelHeight}; surface {region.Width}x{region.Height} DIP; DPI {density.DpiScaleX}x{density.DpiScaleY}; expected {Math.Round(region.Width*density.DpiScaleX)}x{Math.Round(region.Height*density.DpiScaleY)} physical pixels.");
        }
        await DpiChecks.Run(window,host,browserId,output);
        await Script("document.querySelector('.station-brand-launcher .parallax-launcher-trigger').click()");
        await Wait("!!document.querySelector('[role=dialog] .parallax-launcher-search input')","The app switcher did not open above an app surface.");
        for(int i=0;i<100 && host.IsPageVisible(browserId);i++)await Task.Delay(20);
        if(host.IsPageVisible(browserId))throw new InvalidOperationException("The embedded app obscured the shared switcher.");
        await Capture("shell-header-switcher");
        await host.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyDown\",\"key\":\"Escape\",\"code\":\"Escape\",\"windowsVirtualKeyCode\":27}");
        await Wait("!document.querySelector('[role=dialog]')","Escape did not close the shared switcher.");
        for(int i=0;i<100 && !host.IsPageVisible(browserId);i++)await Task.Delay(20);
        if(!host.IsPageVisible(browserId))throw new InvalidOperationException("Closing the switcher did not restore the app surface.");
        await Wait("document.querySelector('.station-tab.selected .tab-favicon')?.src.startsWith('data:image/png;base64,') && document.querySelector('.station-tab.selected .tab-favicon')?.naturalWidth>0","The native page favicon did not appear on its tab.");
        await Wait("(()=>{const icon=document.querySelector('.station-tab.selected .tab-favicon').getBoundingClientRect(),strip=document.querySelector('.station-tabstrip').getBoundingClientRect();return icon.left>=strip.left && icon.right<=strip.right})()","The selected favicon was outside the visible tab strip.");
        await Capture("shell-tab-favicon");
        int terminalCount = (await Rpc("terminal.list")).GetArrayLength();
        await host.PageCore(browserId).ExecuteScriptAsync("chrome.webview.postMessage({id:'foreign',method:'terminal.create',params:{shell:'cmd'}})"); await Task.Delay(100);
        if ((await Rpc("terminal.list")).GetArrayLength() != terminalCount) throw new InvalidOperationException("A remote app accessed the shell bridge.");
        using (var file = File.Create(Path.Combine(output,"shell-browser-content.png"))) await host.PageCore(browserId).CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file).WaitAsync(TimeSpan.FromSeconds(10));
        await host.PageCore(browserId).ExecuteScriptAsync("window.open('about:blank','_blank')");
        for (int attempt = 0; attempt < 100 && host.BrowserPageCount != 2; attempt++) await Task.Delay(100);
        if (host.BrowserPageCount != 2) throw new InvalidOperationException("Browser popup was not retained as an isolated child surface.");
        int beforeAuth = host.BrowserPageCount, beforeExternal=host.ExternalNavigations.Count;
        var external = await Rpc("browser.open",new { url="https://station-check.invalid/login" });
        if (!external.GetProperty("external").GetBoolean() || host.BrowserPageCount != beforeAuth || host.ExternalNavigations.Count != beforeExternal+1) throw new InvalidOperationException("A direct login URL opened inside Station.");
        string popupProof = host.BrowserPageIds.Single(id => id != browserId);
        host.PageCore(popupProof).Navigate("https://station-check.invalid/login?return_to=project");
        await Wait("!!document.querySelector('.tab-pane.active .browser-auth-pending')","Redirected sign-in did not show the browser handoff state.");
        for(int i=0;i<100 && host.ExternalNavigations.Count<beforeExternal+2;i++) await Task.Delay(20);
        string authDocument=await host.PageCore(popupProof).ExecuteScriptAsync("location.href");
        if (host.ExternalNavigations.Count != beforeExternal+2 || authDocument.Contains("/login")) throw new InvalidOperationException($"A redirected login screen loaded inside Station (external={host.ExternalNavigations.Count}, expected={beforeExternal+2}, document={authDocument}).");
        File.WriteAllText(Path.Combine(output,"fixture","credentials.html"),"<!doctype html><title>Credential form fixture</title><form><input type=password></form>");
        await Rpc("browser.navigate",new { id=popupProof,url="https://station-check.invalid/credentials.html" });
        for(int i=0;i<100 && host.ExternalNavigations.Count!=beforeExternal+3;i++) await Task.Delay(50);
        if(host.ExternalNavigations.Count!=beforeExternal+3) throw new InvalidOperationException("A credential form was not routed to the default browser.");
        await Wait("!!document.querySelector('.tab-pane.active .browser-auth-pending')","A credential form remained visible in Station.");
        await Click(".station-tab [role=tab]","Terminal");
        await Wait("(()=>{const tab=document.querySelector('[aria-label=\"Move Terminal to new window\"]').closest('.station-tab').getBoundingClientRect(),strip=document.querySelector('.station-tabstrip').getBoundingClientRect();return tab.left+35>=strip.left && tab.left+35<strip.right})()","The terminal tab’s drag target did not scroll into view.");
        // Drive actual Chromium pointer input, including pointer capture.
        var dragBox = JsonDocument.Parse(await Script("(()=>{const r=document.querySelector('[aria-label=\"Move Terminal to new window\"]').closest('.station-tab').getBoundingClientRect();return {x:r.left+35,y:r.top+r.height/2}})()")).RootElement;
        double dragX=dragBox.GetProperty("x").GetDouble(), dragY=dragBox.GetProperty("y").GetDouble();
        await host.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new { type="mousePressed",x=dragX,y=dragY,button="left",clickCount=1 }));
        await host.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new { type="mouseMoved",x=600,y=350,button="left",buttons=1 }));
        await host.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new { type="mouseReleased",x=600,y=350,button="left",clickCount=1 }));
        for (int i=0;i<300 && host.LastDetachedWindow is null;i++) await Task.Delay(100);
        var detached = host.LastDetachedWindow ?? throw new InvalidOperationException("Moving a terminal tab did not create a window.");
        try
        {
            var child = detached.WebHost;
            for (int i=0;i<100 && await child.Browser.ExecuteScriptAsync("window.station.readTerminal('" + first + "').includes('WEB_CWD_PRESERVED')") != "true";i++) await Task.Delay(100);
            if (await child.Browser.ExecuteScriptAsync("window.station.readTerminal('" + first + "').includes('WEB_CWD_PRESERVED')") != "true") throw new InvalidOperationException("Moving the terminal lost its screen.");
            var movedSessions = JsonSerializer.SerializeToElement(await child.Runtime.Call("terminal.list",JsonSerializer.SerializeToElement(new { })));
            if (movedSessions[0].GetProperty("processId").GetInt32()!=process || (await Rpc("terminal.list")).GetArrayLength()!=0) throw new InvalidOperationException("Moving terminal restarted or duplicated its process.");
            if (movedSessions.EnumerateArray().Single(session => session.GetProperty("shell").GetString()=="powershell").GetProperty("processId").GetInt32()!=powerShellProcess) throw new InvalidOperationException("Moving the PowerShell terminal restarted its process.");
            for (int i=0;i<100 && await child.Browser.ExecuteScriptAsync("window.station.readTerminal('" + powerShellSession + "').includes('WEB_PS_STATE_42')") != "true";i++) await Task.Delay(100);
            if (await child.Browser.ExecuteScriptAsync("document.querySelector('[aria-label=\"Terminal shell profile\"]').value==='powershell' && window.station.readTerminal('" + powerShellSession + "').includes('WEB_PS_STATE_42')") != "true") throw new InvalidOperationException("PowerShell profile selection or screen did not survive moving its tab.");
            await child.Runtime.Call("terminal.input",JsonSerializer.SerializeToElement(new { id=powerShellSession,data="Write-Output ('WEB_PS_MOVED_' + $StationProof)\r" }));
            for (int i=0;i<100 && await child.Browser.ExecuteScriptAsync("window.station.readTerminal('" + powerShellSession + "').includes('WEB_PS_MOVED_41')") != "true";i++) await Task.Delay(100);
            if (await child.Browser.ExecuteScriptAsync("window.station.readTerminal('" + powerShellSession + "').includes('WEB_PS_MOVED_41')") != "true") throw new InvalidOperationException("Moved PowerShell lost its interactive session state.");
            await child.Runtime.Call("terminal.input",JsonSerializer.SerializeToElement(new { id=first,data="printf 'WEB_MOVED_%s\\n' OK\r" }));
            for (int i=0;i<100 && await child.Browser.ExecuteScriptAsync("window.station.readTerminal('" + first + "').includes('WEB_MOVED_OK')") != "true";i++) await Task.Delay(100);
            if (await child.Browser.ExecuteScriptAsync("window.station.readTerminal('" + first + "').includes('WEB_MOVED_OK')") != "true") throw new InvalidOperationException("Moved terminal cannot execute commands.");
            await child.Browser.ExecuteScriptAsync("document.querySelector('.station-tab.selected').dispatchEvent(new MouseEvent('auxclick',{button:1,bubbles:true,cancelable:true}))");
            for (int i=0;i<100;i++) { var list=JsonSerializer.SerializeToElement(await child.Runtime.Call("terminal.list",JsonSerializer.SerializeToElement(new { }))); if(list.GetArrayLength()==0)break; await Task.Delay(50); }
            var remaining=JsonSerializer.SerializeToElement(await child.Runtime.Call("terminal.list",JsonSerializer.SerializeToElement(new { })));
            if(remaining.GetArrayLength()!=0) throw new InvalidOperationException("Middle-click did not close the terminal tab and its process.");
        }
        finally { detached.Close(); }
        try { using var stopped = Process.GetProcessById(process); if (!stopped.WaitForExit(5000)) throw new InvalidOperationException("Native terminal process survived closing its React tab."); } catch (ArgumentException) { }
        try { using var stopped = Process.GetProcessById(powerShellProcess); if (!stopped.WaitForExit(5000)) throw new InvalidOperationException("Native PowerShell process survived closing its React tab."); } catch (ArgumentException) { }
        var browserCore = host.PageCore(browserId);
        await browserCore.ExecuteScriptAsync("window.__moveProof='unsaved-page-state'");
        await Script("document.querySelector('[data-tab-id=\"browser:" + browserId + "\"] .tab-detach').click()");
        for (int i=0;i<200 && ReferenceEquals(host.LastDetachedWindow,detached);i++) await Task.Delay(100);
        var browserWindow = host.LastDetachedWindow;
        if (browserWindow is null || ReferenceEquals(browserWindow,detached)) throw new InvalidOperationException("Browser popout did not open.");
        try
        {
            if (!ReferenceEquals(browserWindow.WebHost.PageCore(browserId),browserCore) || await browserCore.ExecuteScriptAsync("window.__moveProof") != "\"unsaved-page-state\"") throw new InvalidOperationException("Moving a browser tab reloaded its page.");
            await browserCore.ExecuteScriptAsync("chrome.webview.postMessage({id:'foreign-moved',method:'terminal.create',params:{shell:'cmd'}})");
            await Task.Delay(100);
            var count=JsonSerializer.SerializeToElement(await browserWindow.WebHost.Runtime.Call("terminal.list",JsonSerializer.SerializeToElement(new { })));
            if(count.GetArrayLength()!=0) throw new InvalidOperationException("A moved remote app accessed the native bridge.");
        }
        finally { browserWindow.Close(); }
        // Preserve the existing independent-process journal acceptance gate.
        var identity = new WorkspaceIdentity("https://station-contract.invalid",41,11,"Test organization A"); var store = new WorkspaceStore(Path.Combine(output,"context-contract"));
        store.State.SelectedWorkspace = identity.Key; var visit = store.Visit(new(identity,"slipstream","workstream",73),"Persistence test workstream"); visit.Pinned = true; visit.SeenEvent = 108; store.Save();
        File.WriteAllText(Path.Combine(output,"acceptance.json"),JsonSerializer.Serialize(new { kind = "react-station-native-host", nativeWindows = true, webShell = true, runtime = Environment.Version.ToString(), checks = new { helmFrame = true, coreGrid = true, persistentTabs = true, nativePty = true, wslDefault = true, wslToggleAndWindowsProfile = true, powerShellNativeProfile = true, powerShellProcessAndStateSurvivePopout = true, ubuntuProfileArtworkAndTheme = true, commandExecution = true, secretNoEcho = true, ctrlC = true, resize = true, independentSessions = true, workingDirectory = true, cleanup = true, nativeTelemetry = true, remoteBridgeIsolation = true, popupSurfaces = true, terminalPopoutPreservesProcessAndScreen = true, middleClickClosesTab = true, browserPopoutPreservesPageAndIsolation = true, projectAndWorkstreamContract = contractFixture, expandProjectLoadsTasks = contractFixture, legacyProjectTaskRead = contractFixture, startupTlsRecovery = contractFixture, sharedSignInRequiresAdvertisedCapability = true, canceledAppInitializationRecovery = true, overlappingAppNavigationRecovery = true, appPixelsCompositedAboveShell = true, sharedHeaderAppSwitcher = true, appSwitcherRestoresBrowser = true, browserSurfacesPixelAligned = true, perMonitorPixelDensity = true, noBrowserLinkStatusFooter = true, pageFaviconsOnTabs = true, infrastructureServiceGrouping = true, loginsOpenInDefaultBrowser = true, browserPreference = true, browserActivationAndPkce = true, slipstreamUsesExistingConnection = contractFixture, nexusOptIn = contractFixture, unravelAppEntry = true, noStandaloneFiles = true } },new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output,"terminal-powershell.json"),JsonSerializer.Serialize(new { profile = powerShellName, processId = powerShellProcess, sessionId = powerShellSession, nativeProcess = powerShellName == "PowerShell 7" ? "pwsh" : "powershell", history = "SaveNothing", processAndVariablePreservedAfterPopout = true, closedWithTab = true },new JsonSerializerOptions { WriteIndented = true }));
        string failure = Path.Combine(output,"failure.txt"); if (File.Exists(failure)) { Directory.CreateDirectory(Path.Combine(output,"history")); File.Move(failure,Path.Combine(output,"history","failure-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt"),true); }
    }
    private sealed class FixtureApi : HttpMessageHandler
    {
        private readonly string project,work;
        private readonly long userId,teamId;
        private readonly bool legacyProject;
        private readonly int startupFailures;
        private int meCalls;
        public FixtureApi(string output,bool legacyProject = false,int startupFailures = 0)
        {
            this.legacyProject = legacyProject; this.startupFailures=startupFailures;
            project = File.ReadAllText(Path.Combine(output,"project-hierarchy-contract.json")); using var parsed = JsonDocument.Parse(project);
            userId = parsed.RootElement.GetProperty("project").GetProperty("user_id").GetInt64(); teamId = parsed.RootElement.GetProperty("project").GetProperty("team_id").GetInt64();
            var data = JsonNode.Parse(File.ReadAllText(Path.Combine(output,"slipstream-api-contract.json")))!; data["workstream"]!["team_id"] = teamId; data["workstream"]!["user_id"] = userId; work = data.ToJsonString();
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.PathAndQuery; string response;
            using var parsed = JsonDocument.Parse(project); var record = parsed.RootElement.GetProperty("project");
            if(path=="/api/v1/me" && Interlocked.Increment(ref meCalls)<=startupFailures) throw new HttpRequestException(HttpRequestError.SecureConnectionError,"Synthetic SSL startup failure.",new System.Security.Authentication.AuthenticationException("Synthetic TLS handshake unavailable."));
            if (path == "/api/v1/me") response = JsonSerializer.Serialize(new { user = new { id = userId,name = "Station contract fixture",initials = "ST" }, teams = new[] { new { id = teamId,name = "Hierarchy contract fixture",personal_team = false } } });
            else if (path == "/api/v1/projects?navigation=1") response = JsonSerializer.Serialize(new { projects = new[] { record } });
            else if (path == "/api/v1/projects" && legacyProject)
            {
                var node = JsonNode.Parse(record.GetRawText())!; var lane = JsonNode.Parse(work)!["workstream"]!; lane["project_id"] = record.GetProperty("id").GetInt64();
                node["workstreams"] = new JsonArray(lane.DeepClone()); response = new JsonObject { ["projects"] = new JsonArray(node) }.ToJsonString();
            }
            else if (path.StartsWith("/api/v1/projects/"))
            { if (legacyProject) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); response = project; }
            else if (path == "/api/v1/workstreams") { using var payload = JsonDocument.Parse(work); response = JsonSerializer.Serialize(new { workstreams = new[] { payload.RootElement.GetProperty("workstream") } }); }
            else if (path == "/api/v1/dashboard") response = "{\"attention\":[],\"workstreams\":[],\"deck\":{},\"fleet\":{}}";
            else if (path.StartsWith("/api/v1/workstreams/")) response = work;
            else return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response,Encoding.UTF8,"application/json") });
        }
    }
}
