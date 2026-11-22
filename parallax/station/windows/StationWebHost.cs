using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StationWebView = Microsoft.Web.WebView2.Wpf.WebView2CompositionControl;

namespace Parallax.Station;

public sealed class StationWebHost : Grid, IDisposable
{
    internal const string Address = "https://station-shell.invalid/index.html";
    internal StationWebView Browser { get; } = new() { UseLayoutRounding=true, SnapsToDevicePixels=true };
    internal StationRuntime Runtime { get; }
    internal Task Ready => ready.Task;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string directory;
    private readonly bool isolated;
    internal Func<string,Task<CoreWebView2Environment>>? TestEnvironmentFactory { get; set; }
    internal bool LiveSignInCheck { get; set; }
    internal bool UseSavedBrowserProfileForCheck { get; set; }
    internal object PageFrameMetrics(string id)
    {
        System.Windows.Controls.Image? FindImage(DependencyObject visual)
        {
            if(visual is System.Windows.Controls.Image image)return image;
            for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(visual);i++)
            { var child=FindImage(System.Windows.Media.VisualTreeHelper.GetChild(visual,i));if(child is not null)return child; }
            return null;
        }
        var view=pages[id].Control;var frame=FindImage(view);var source=frame?.Source as System.Windows.Interop.D3DImage;
        return new { frameType=frame?.Source?.GetType().Name,pixelWidth=source?.PixelWidth,pixelHeight=source?.PixelHeight,imageWidth=frame?.ActualWidth,imageHeight=frame?.ActualHeight,stretch=frame?.Stretch.ToString() };
    }
    internal Rect PageBounds(string id) => new(pages[id].Control.TranslatePoint(new Point(),this),pages[id].Control.RenderSize);
    internal bool IsPageVisible(string id) => pages[id].Control.Visibility == Visibility.Visible && ReferenceEquals(activePage,pages[id]);
    internal Action<string>? SignInTrace { get; set; }
    internal Task<object> CheckApplication(string address)
    {
        if (!isolated || !LiveSignInCheck) throw new InvalidOperationException("Live handoff checks require an isolated diagnostic profile.");
        return BrowserAction("browser.open",JsonSerializer.SerializeToElement(new { url=address,forceInside=true }));
    }
    internal int BrowserPageCount => pages.Count;
    internal string[] BrowserPageIds => pages.Keys.ToArray();
    internal CoreWebView2 PageCore(string id) => pages[id].Control.CoreWebView2;
    private readonly Dictionary<string,RemoteView> pages = new();
    private readonly Dictionary<string,Task<CoreWebView2Environment>> environments = new();
    private readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private RemoteView? activePage;
    private bool disposed, fullScreen, overlayVisible;
    internal List<Uri> ExternalNavigations { get; } = new();
    private WindowState previousWindowState;
    public StationWebHost(string? testDirectory,StationRuntime? runtime = null)
    {
        isolated = testDirectory is not null;
        directory = testDirectory ?? StationServices.StateDirectory;
        Runtime = runtime ?? new(testDirectory); Runtime.Event += Send;
        UseLayoutRounding=true;SnapsToDevicePixels=true;
        Children.Add(Browser);
        Loaded += async (_, _) =>
        {
            if (Browser.CoreWebView2 is not null) return;
            try { await Initialize(); }
            catch (Exception error) { ready.TrySetException(error); MessageBox.Show(error.Message,"Station could not open"); }
        };
    }
    private async Task Initialize()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null,Path.Combine(directory,"shell-profile"));
        await Browser.EnsureCoreWebView2Async(environment);
        var core = Browser.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("station-shell.invalid",Path.Combine(AppContext.BaseDirectory,"Assets","Shell"),CoreWebView2HostResourceAccessKind.DenyCors);
        if (isolated)
        {
            core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, e) => File.AppendAllText(Path.Combine(directory,"shell-errors.jsonl"),e.ParameterObjectAsJson + Environment.NewLine);
            await core.CallDevToolsProtocolMethodAsync("Runtime.enable","{}");
        }
        core.NavigationCompleted += (_, e) => { if (!e.IsSuccess) ready.TrySetException(new IOException("Shell failed to load: " + e.WebErrorStatus)); };
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.NavigationStarting += (_, e) => { if (e.Uri != Address) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.WebMessageReceived += async (_, e) =>
        {
            // A remote sub-app is a separate WebView. It can never originate
            // messages on this local-only document's capability bridge.
            if (disposed || e.Source != Address || e.WebMessageAsJson.Length > 1000000) return;
            string? id = null;
            try
            {
                using var document = JsonDocument.Parse(e.WebMessageAsJson); var message = document.RootElement;
                if (message.TryGetProperty("type",out var type) && type.GetString() == "ready") { ready.TrySetResult(true); return; }
                id = message.GetProperty("id").GetString(); string method = message.GetProperty("method").GetString()!; var p = message.GetProperty("params").Clone();
                object result;
                if (method == "window.detach") result = await Detach(p);
                else if (method.StartsWith("browser.",StringComparison.Ordinal)) result = await BrowserAction(method,p);
                else if (method == "clipboard.copy") { Clipboard.SetText(p.GetProperty("text").GetString() ?? ""); result = new { copied = true }; }
                else if (method == "clipboard.paste") result = new { text = Clipboard.ContainsText() ? Clipboard.GetText() : "" };
                else result = await Runtime.Call(method,p);
                Send(new { id, ok = true, data = result });
            }
            catch (Exception error) { Send(new { id, ok = false, error = error.Message, accessDenied = error is ServiceAccessException, authenticationRequired = error is ServiceAccessException { StatusCode: 401 } }); }
        };
        WireShortcuts(Browser,false);
        core.Navigate(Address);
    }
    internal MainWindow? LastDetachedWindow { get; private set; }
    private async Task<object> Detach(JsonElement p)
    {
        var tab = p.GetProperty("tab");
        string type = tab.GetProperty("type").GetString()!;
        if (type == "home" || !new[] { "apps","connect","project","workstream","terminal","machine","settings","browser","slipstream" }.Contains(type)) throw new InvalidOperationException("This tab cannot be moved.");
        string workspace = tab.GetProperty("workspace").GetString()!;
        if (workspace != Runtime.ContextKey) throw new InvalidOperationException("Select the tab's organization before moving it.");
        var source = Window.GetWindow(this)!;
        var sibling = new MainWindow(isolated ? directory : null,Runtime.CreateSibling());
        sibling.WindowStartupLocation = WindowStartupLocation.Manual;
        sibling.Left = source.Left + 48; sibling.Top = source.Top + 48;
        sibling.Show();
        var target = sibling.WebHost;
        bool moved = false;
        try
        {
            await target.Ready.WaitAsync(TimeSpan.FromSeconds(30));
            // Pause delivery before the xterm write barrier and screen snapshot.
            if (type == "terminal") Runtime.PauseTerminals(workspace);
            await Browser.ExecuteScriptAsync("window.__tabTransfer=null;window.station.captureTab(" + tab.GetRawText() + ").then(value=>window.__tabTransfer={value},error=>window.__tabTransfer={error:error.message})");
            string snapshot = "null";
            for (int i = 0; i < 300; i++) { snapshot = await Browser.ExecuteScriptAsync("window.__tabTransfer"); if (snapshot != "null") break; await Task.Delay(25); }
            using var captured = JsonDocument.Parse(snapshot);
            if (captured.RootElement.ValueKind == JsonValueKind.Null) throw new TimeoutException("Tab snapshot timed out.");
            if (captured.RootElement.TryGetProperty("error",out var captureError)) throw new InvalidOperationException(captureError.GetString());
            snapshot = captured.RootElement.GetProperty("value").GetRawText();
            if (type == "terminal") Runtime.MoveTerminalsTo(target.Runtime,workspace);
            if (type == "browser")
            {
                string id = tab.GetProperty("browserId").GetString()!;
                if (!pages.Remove(id,out var page)) throw new InvalidOperationException("Browser tab is closed.");
                Children.Remove(page.Control); page.Control.Visibility = Visibility.Collapsed;
                page.Owner = target; target.pages.Add(id,page); target.Children.Add(page.Control);
            }
            moved = true;
            await target.Browser.ExecuteScriptAsync("window.station.receiveTab(" + snapshot + ")");
            LastDetachedWindow = sibling; sibling.Activate();
            return new { moved = true };
        }
        catch
        {
            if (moved && type == "terminal") target.Runtime.MoveTerminalsTo(Runtime,workspace);
            if (moved && type == "browser")
            {
                string id = tab.GetProperty("browserId").GetString()!;
                if (target.pages.Remove(id,out var page)) { target.Children.Remove(page.Control); page.Owner = this; pages[id] = page; Children.Add(page.Control); }
            }
            if (type == "terminal") Runtime.ResumeTerminals(workspace);
            sibling.Close(); throw;
        }
    }
    private void WireShortcuts(StationWebView control,bool remote,Func<StationWebHost>? currentOwner = null)
    {
        control.PreviewKeyDown += (_, e) =>
        {
            var owner = currentOwner?.Invoke() ?? this;
            bool ctrl = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
            if (e.Key == System.Windows.Input.Key.F11)
            {
                e.Handled = true; Dispatcher.BeginInvoke(new Action(() =>
                {
                    var window = Window.GetWindow(owner); if (window is null) return;
                    if (!owner.fullScreen) { owner.previousWindowState = window.WindowState; window.WindowStyle = WindowStyle.None; window.WindowState = WindowState.Maximized; }
                    else { window.WindowStyle = WindowStyle.SingleBorderWindow; window.WindowState = owner.previousWindowState; }
                    owner.fullScreen = !owner.fullScreen;
                }));
            }
            else if (ctrl && (e.Key == System.Windows.Input.Key.K || (remote && e.Key == System.Windows.Input.Key.W)))
            {
                e.Handled = true; string script = e.Key == System.Windows.Input.Key.K ? "window.station.focusSearch()" : "window.station.closeCurrent()";
                Dispatcher.BeginInvoke(new Action(async () => { if (!owner.disposed) await owner.Browser.ExecuteScriptAsync(script); }));
            }
        };
    }
    private void Send(object message)
    {
        if (disposed || Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Send(message))); return; }
        if (Browser.CoreWebView2 is not null) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message,json));
    }
    private async Task<object> BrowserAction(string method,JsonElement p)
    {
        if (method == "browser.preferences") return BrowserSettings.Read(Runtime);
        if (method == "browser.preferences.save") return BrowserSettings.Save(Runtime,p);
        if (method == "browser.defaultSettings") { if(!isolated) BrowserSettings.OpenSystemSettings(); return new { opened = true }; }
        if (method == "browser.registerLinks") { if(!isolated) StationLinks.Register(); return new { registered = true }; }
        if (method == "browser.open")
        {
            var uri = Runtime.ApplicationAddress(StationServices.ValidateWebAddress(p.GetProperty("url").GetString()!),Runtime.ContextKey);
            if (StationServices.IsAuthenticationAddress(uri) || (Runtime.Store.State.BrowserLinkMode == "external" && !(p.TryGetProperty("forceInside",out var force) && force.ValueKind == JsonValueKind.True))) { OpenExternal(uri); return new { external = true }; }
            string id = Guid.NewGuid().ToString("N"); var view = await CreatePage(id,Runtime.ContextKey); view.RequestedUrl = uri.AbsoluteUri;
            if (!await SharedSignIn(view,uri)) view.Control.CoreWebView2.Navigate(uri.AbsoluteUri); return new { id, url = uri.AbsoluteUri, workspace = view.Workspace,authenticationRequired = view.AuthenticationRequired,favicon=view.Favicon };
        }
        if (method == "browser.overlay")
        {
            overlayVisible=p.GetProperty("visible").GetBoolean();
            foreach(var candidate in pages.Values) candidate.Control.Visibility = !overlayVisible && ReferenceEquals(activePage,candidate) && !candidate.AuthenticationRequired && !candidate.Checking ? Visibility.Visible : Visibility.Collapsed;
            return new { visible=overlayVisible };
        }
        if (method == "browser.hide") { HidePages(); return new { hidden = true }; }
        string key = p.GetProperty("id").GetString()!;
        if (!pages.TryGetValue(key,out var page) || page.Workspace != Runtime.ContextKey) throw new InvalidOperationException("Browser tab belongs to another organization or is closed.");
        var core = page.Control.CoreWebView2;
        switch (method)
        {
            case "browser.bounds":
                HidePages();
                var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this);
                double scaleX=dpi.DpiScaleX,scaleY=dpi.DpiScaleY;
                double x = Math.Clamp(p.GetProperty("x").GetDouble(),0,ActualWidth), y = Math.Clamp(p.GetProperty("y").GetDouble(),0,ActualHeight);
                double right=Math.Clamp(x+p.GetProperty("width").GetDouble(),x,ActualWidth),bottom=Math.Clamp(y+p.GetProperty("height").GetDouble(),y,ActualHeight);
                // The captured browser image must land on physical pixel edges;
                // fractional CSS bounds otherwise interpolate the rendered text.
                x=Math.Round(x*scaleX)/scaleX;y=Math.Round(y*scaleY)/scaleY;
                right=Math.Round(right*scaleX)/scaleX;bottom=Math.Round(bottom*scaleY)/scaleY;
                page.Control.Margin = new Thickness(x,y,0,0);
                page.Control.Width = Math.Max(1/scaleX,right-x);
                page.Control.Height = Math.Max(1/scaleY,bottom-y);
                page.Control.Visibility = overlayVisible || page.AuthenticationRequired || page.Checking ? Visibility.Collapsed : Visibility.Visible; activePage = page; return new { shown = !page.AuthenticationRequired };
            case "browser.navigate":
                var next = Runtime.ApplicationAddress(StationServices.ValidateWebAddress(p.GetProperty("url").GetString()!),page.Workspace);
                if (StationServices.IsAuthenticationAddress(next)) { RequireAuthentication(page,key,next); break; }
                page.RequestedUrl = next.AbsoluteUri; page.AuthenticationRequired = false;
                Send(new { type = "browser.state",id = key,workspace = page.Workspace,authenticationRequired = false,error = (string?)null });
                if (!await SharedSignIn(page,next)) core.Navigate(next.AbsoluteUri); break;
            case "browser.external": OpenExternal(StationServices.ValidateWebAddress(page.AuthenticationRequired ? page.RequestedUrl : core.Source)); break;
            case "browser.back": if (core.CanGoBack) core.GoBack(); break;
            case "browser.forward": if (core.CanGoForward) core.GoForward(); break;
            case "browser.reload": core.Reload(); break;
            case "browser.close": page.AuthCancellation?.Cancel(); Children.Remove(page.Control); page.Control.Dispose(); pages.Remove(key); if (activePage == page) activePage = null; break;
            default: throw new InvalidOperationException("Unsupported browser action.");
        }
        return new { done = true };
    }
    private void OpenExternal(Uri uri)
    {
        if (isolated) { ExternalNavigations.Add(uri); return; }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    private void RequireAuthentication(RemoteView page,string id,Uri login)
    {
        if (page.AuthenticationRequired) return;
        if(!Uri.TryCreate(page.RequestedUrl,UriKind.Absolute,out var requested) || requested.Scheme!="https") page.RequestedUrl=login.AbsoluteUri;
        page.AuthenticationRequired = true; page.Control.Visibility = Visibility.Collapsed;
        Send(new { type = "browser.state",id,workspace = page.Workspace,authenticationRequired = true,error = (string?)null,loading = false });
        _ = CompleteAuthentication(page,id,login);
    }
    private async Task CompleteAuthentication(RemoteView page,string id,Uri login)
    {
        try
        {
            if (await TryAppAuthentication(page,id)) return;
            if(!page.Owner.disposed && page.Owner.pages.ContainsKey(id)) page.Owner.OpenExternal(login);
        }
        catch (Exception) { if(!page.Owner.disposed) page.Owner.Send(new { type = "notice",message = "Sign-in could not finish. Continue in your browser or try again." }); }
    }
    private async Task<bool> TryAppAuthentication(RemoteView page,string id)
    {
        if(isolated || !Uri.TryCreate(page.RequestedUrl,UriKind.Absolute,out var target) || target.Scheme!="https" || target.GetLeftPart(UriPartial.Authority)!="https://unravel.tools") return false;
        bool opened = false;
        page.AuthCancellation = new CancellationTokenSource();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(page.AuthCancellation.Token); timeout.CancelAfter(TimeSpan.FromSeconds(180));
        using var callback = new BrowserAuthCallback();
        var core=page.Control.CoreWebView2; page.HandoffActive = true;
        try
        {
            string origin=target.GetLeftPart(UriPartial.Authority);
            string start=origin+"/station/auth/start?redirect_uri="+Uri.EscapeDataString(callback.Address.AbsoluteUri)+"&state="+callback.State+"&code_challenge="+callback.Challenge+"&code_challenge_method=S256&target="+Uri.EscapeDataString(target.PathAndQuery+target.Fragment);
            var prepared=await NavigateAndWait(core,start,()=>core.Navigate(start),timeout.Token);
            if(!prepared || new Uri(core.Source).GetLeftPart(UriPartial.Authority)!=origin || new Uri(core.Source).AbsolutePath!="/station/auth/start") return false;
            string raw=await core.ExecuteScriptAsync("({flow:document.querySelector('meta[name=station-challenge]')?.content,authorize:document.querySelector('meta[name=station-authorize]')?.content,csrf:document.querySelector('meta[name=station-csrf]')?.content||document.querySelector('meta[name=csrf-token]')?.content})");
            using var meta=JsonDocument.Parse(raw); var data=meta.RootElement;
            if(!data.TryGetProperty("flow",out var flowValue) || !data.TryGetProperty("authorize",out var authorizeValue) || !data.TryGetProperty("csrf",out var csrfValue))return false;
            string flow=flowValue.GetString()??"",csrf=csrfValue.GetString()??"";
            if(flow.Length!=64 || flow.Any(c=>!char.IsAsciiLetterOrDigit(c)) || csrf.Length is < 16 or > 256)return false;
            var authorize=StationServices.ValidateWebAddress(authorizeValue.GetString()??"");
            if(authorize.AbsoluteUri!=origin+"/station/auth/authorize/"+flow)return false;
            page.Owner.OpenExternal(authorize); opened = true;
            string code=await callback.Receive(timeout.Token);
            string form="_token="+Uri.EscapeDataString(csrf)+"&flow="+Uri.EscapeDataString(flow)+"&code="+Uri.EscapeDataString(code)+"&code_verifier="+Uri.EscapeDataString(callback.Verifier);
            page.RequestBody?.Dispose(); page.RequestBody=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(form));
            string complete=origin+"/station/auth/complete";
            var request=core.Environment.CreateWebResourceRequest(complete,"POST",page.RequestBody,"Content-Type: application/x-www-form-urlencoded\r\n");
            if(!await NavigateAndWait(core,complete,()=>core.NavigateWithWebResourceRequest(request),timeout.Token))return opened;
            if(StationServices.IsAuthenticationAddress(new Uri(core.Source)))return opened;
            page.AuthenticationRequired=false; page.Checking=false;
            page.Owner.Send(new { type="browser.state",id,workspace=page.Workspace,authenticationRequired=false,url=core.Source,title=core.DocumentTitle,error=(string?)null });
            Window.GetWindow(page.Owner)?.Activate(); return true;
        }
        catch(Exception error) when(error is OperationCanceledException or IOException or InvalidOperationException or JsonException or ArgumentException) { return opened; }
        finally { page.HandoffActive=false; page.AuthCancellation.Dispose(); page.AuthCancellation=null; _=UpdateFavicon(page,id); }
    }
    internal static async Task<bool> NavigateAndWait(CoreWebView2 core,string address,Action navigate,CancellationToken cancellation)
    {
        ulong? navigation=null; var result=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Starting(object? sender,CoreWebView2NavigationStartingEventArgs e) { if(e.Uri==address)navigation=e.NavigationId; }
        void Completed(object? sender,CoreWebView2NavigationCompletedEventArgs e) { if(navigation==e.NavigationId)result.TrySetResult(e.IsSuccess && e.HttpStatusCode<400); }
        core.NavigationStarting+=Starting; core.NavigationCompleted+=Completed;
        try { navigate(); return await result.Task.WaitAsync(cancellation); }
        finally { core.NavigationStarting-=Starting; core.NavigationCompleted-=Completed; }
    }
    private static readonly HashSet<string> FirstPartyOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "https://continuum.ceo", "https://slipstream.codes", "https://plexus.team", "https://positronic.app",
        "https://my.parallaxweb.services", "https://nebula.parallax.link", "https://nucleus.parallax.link"
    };
    // These routes may redirect through existing OAuth without showing a login document.
    // NavigationCompleted keeps any rendered authentication screen outside Station.
    private static bool IsSilentSignInRoute(Uri uri) =>
        uri.GetLeftPart(UriPartial.Authority) == "https://my.parallaxweb.services" && uri.AbsolutePath == "/oauth/authorize"
        || FirstPartyOrigins.Contains(uri.GetLeftPart(UriPartial.Authority)) && uri.AbsolutePath.TrimEnd('/') == "/login";
    private static readonly SemaphoreSlim SharedSignInGate = new(1,1);
    private async Task<bool> SharedSignIn(RemoteView page,Uri destination)
    {
        if ((isolated && !LiveSignInCheck) || !Runtime.SharedSignInAvailable || !FirstPartyOrigins.Contains(destination.GetLeftPart(UriPartial.Authority))) return false;
        await SharedSignInGate.WaitAsync();
        const string nexus = "https://my.parallaxweb.services";
        var core = page.Control.CoreWebView2;
        try
        {
            page.SharedSession = true;
            string prepare=nexus + "/station/session/start?target=" + Uri.EscapeDataString(destination.AbsoluteUri) + "&subject=" + Runtime.Workspace!.UserId;
            using var prepareTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            if(!await NavigateAndWait(core,prepare,()=>core.Navigate(prepare),prepareTimeout.Token))
                throw new IOException("The shared sign-in navigation did not finish. Open the app again to retry.");
            var landed = new Uri(core.Source);
            if (landed.GetLeftPart(UriPartial.Authority) == destination.GetLeftPart(UriPartial.Authority) && !StationServices.IsAuthenticationAddress(landed)) { page.SharedSession = true; return true; }
            if (new Uri(core.Source).GetLeftPart(UriPartial.Authority) != nexus) throw new InvalidOperationException("Unexpected shared sign-in destination.");
            string raw = await core.ExecuteScriptAsync("document.querySelector('meta[name=station-challenge]')?.content||null");
            string? challenge = JsonSerializer.Deserialize<string>(raw);
            if (challenge is null || challenge.Length != 64 || challenge.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new InvalidOperationException("The Nexus shared sign-in service needs to be deployed.");
            SignInTrace?.Invoke("Requesting scoped session ticket");
            string ticket = await Runtime.SharedSessionTicket(challenge);
            SignInTrace?.Invoke("Scoped session ticket received");
            page.RequestBody?.Dispose();
            var body = page.RequestBody = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("ticket=" + Uri.EscapeDataString(ticket)));
            var request = core.Environment.CreateWebResourceRequest(nexus + "/station/session/complete","POST",body,"Content-Type: application/x-www-form-urlencoded\r\n");
            page.SharedSession = true;
            using var completionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (!await NavigateAndWait(core,nexus + "/station/session/complete",() => core.NavigateWithWebResourceRequest(request),completionTimeout.Token))
                throw new InvalidOperationException("Shared sign-in did not complete. Try opening the application again.");
            return true;
        }
        catch (Exception error)
        {
            SignInTrace?.Invoke("Shared sign-in failure: " + error.ToString());
            page.SharedSession = false;
            Send(new { type = "notice",message = "Shared app sign-in is not ready: " + error.Message });
            return false;
        }
        finally { SharedSignInGate.Release(); }
    }
    private void HidePages() { foreach (var page in pages.Values) page.Control.Visibility = Visibility.Collapsed; activePage = null; }
    private static async Task UpdateFavicon(RemoteView page,string id)
    {
        int revision=++page.FaviconRevision;
        var core=page.Control.CoreWebView2;if(core is null || page.Owner.disposed)return;
        string source;
        try{source=core.Source;}catch(Exception error) when(error is InvalidOperationException or System.Runtime.InteropServices.COMException){return;}
        string? data=null;
        try
        {
            if(!string.IsNullOrEmpty(core.FaviconUri))
            {
                using var icon=await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                if(icon is not null)
                {
                    using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
                    while((count=await icon.ReadAsync(buffer))>0)
                    {
                        if(bytes.Length+count>65536)return;
                        bytes.Write(buffer,0,count);
                    }
                    if(bytes.Length>0)data="data:image/png;base64,"+Convert.ToBase64String(bytes.ToArray());
                }
            }
        }
        catch(Exception error) when(error is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException or OperationCanceledException) { return; }
        if(revision!=page.FaviconRevision || page.Owner.disposed || !page.Owner.pages.TryGetValue(id,out var current) || !ReferenceEquals(current,page))return;
        try{if(core.Source!=source)return;}catch(Exception error) when(error is InvalidOperationException or System.Runtime.InteropServices.COMException){return;}
        page.Favicon=data;
        if(!page.HandoffActive)page.Owner.Send(new { type="browser.state",id,workspace=page.Workspace,favicon=data });
    }
    private static bool InitializationCanceled(Exception error) => error is OperationCanceledException || error.HResult == unchecked((int)0x80004004);
    private async Task<RemoteView> CreatePage(string id,string workspace)
    {
        try { return await CreatePageOnce(id,workspace); }
        catch(Exception error) when(!disposed && InitializationCanceled(error))
        {
            // No navigation or authentication write has happened yet. Retry only
            // controller initialization, never session-ticket exchange or submission.
            await Task.Delay(100);
            try { return await CreatePageOnce(id,workspace); }
            catch(Exception retry) when(InitializationCanceled(retry))
            { throw new IOException("Station could not initialize the app view. Open the app again to retry.",retry); }
        }
    }
    private async Task<RemoteView> CreatePageOnce(string id,string workspace)
    {
        // Composition controllers need a measured visual to initialize. Keep the
        // initial about:blank frame one pixel and noninteractive, then hide.
        var control = new StationWebView { Visibility = Visibility.Visible, UseLayoutRounding=true, SnapsToDevicePixels=true, IsHitTestVisible=false, Width=1, Height=1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var page = new RemoteView(control,workspace,this); pages[id] = page; Children.Add(control); SetZIndex(control,10);
        environments.TryGetValue(workspace,out var pending);
        try
        {
            if (pending is null)
            {
                string profile=Path.Combine(LiveSignInCheck && UseSavedBrowserProfileForCheck ? StationServices.StateDirectory : directory,"workspace-profiles",workspace);
                environments[workspace] = pending = isolated && TestEnvironmentFactory is not null ? TestEnvironmentFactory(profile) : CoreWebView2Environment.CreateAsync(null,profile);
            }
            await control.EnsureCoreWebView2Async(await pending);
            control.Visibility=Visibility.Collapsed;control.IsHitTestVisible=true;
            var core = control.CoreWebView2;
            core.Settings.IsStatusBarEnabled=false;
            WireShortcuts(control,true,() => page.Owner);
            if (isolated) core.SetVirtualHostNameToFolderMapping("station-check.invalid",Path.Combine(directory,"fixture"),CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) =>
            {
                page.NavigationId = e.NavigationId;
                if(!e.IsRedirected)
                {
                    page.FaviconRevision++;page.Favicon=null;
                    if(!page.HandoffActive)page.Owner.Send(new {type="browser.state",id,workspace,favicon=(string?)null});
                }
                if (Uri.TryCreate(e.Uri,UriKind.Absolute,out var tracingUri)) SignInTrace?.Invoke("Navigation " + e.NavigationId + " " + tracingUri.GetLeftPart(UriPartial.Path));
                if (e.Uri == "about:blank") return;
                try
                {
                    var uri = StationServices.ValidateWebAddress(e.Uri);
                    if (StationServices.IsAuthenticationAddress(uri) && !(page.SharedSession && IsSilentSignInRoute(uri))) { e.Cancel = true; page.Owner.RequireAuthentication(page,id,uri); return; }
                    page.Checking = true; control.Visibility = Visibility.Collapsed;
                }
                catch { e.Cancel = true; }
            };
            core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
            core.SourceChanged += (_, _) => { if(!page.HandoffActive) page.Owner.Send(new { type = "browser.state", id, workspace, url = core.Source, canGoBack = core.CanGoBack, canGoForward = core.CanGoForward }); };
            core.HistoryChanged += (_, _) => page.Owner.Send(new { type = "browser.state",id,workspace,canGoBack = core.CanGoBack,canGoForward = core.CanGoForward });
            core.FaviconChanged += async (_, _) => await UpdateFavicon(page,id);
            core.DocumentTitleChanged += (_, _) => { if(!page.HandoffActive)page.Owner.Send(new { type = "browser.state", id, workspace, title = core.DocumentTitle }); };
            core.NavigationCompleted += async (_, e) =>
            {
                SignInTrace?.Invoke("Completed " + e.NavigationId + " success=" + e.IsSuccess + " status=" + e.HttpStatusCode + " error=" + e.WebErrorStatus);
                if (e.NavigationId != page.NavigationId) return;
                page.RequestBody?.Dispose(); page.RequestBody = null;
                if (page.AuthenticationRequired) return;
                if (e.IsSuccess && Uri.TryCreate(core.Source,UriKind.Absolute,out var completedUri) && completedUri.Scheme == "https" && StationServices.IsAuthenticationAddress(completedUri)) { page.Owner.RequireAuthentication(page,id,completedUri); return; }
                if (e.HttpStatusCode is 401 or 403) { page.Owner.RequireAuthentication(page,id,StationServices.ValidateWebAddress(page.RequestedUrl)); return; }
                try
                {
                    // Keep the surface hidden until a credential form has been ruled out.
                    if (e.IsSuccess && await core.ExecuteScriptAsync("!!document.querySelector('input[type=password]')") == "true")
                    { page.Owner.RequireAuthentication(page,id,StationServices.ValidateWebAddress(core.Source)); return; }
                }
                catch (Exception) { if (page.Owner.disposed) return; }
                if (e.NavigationId != page.NavigationId || page.AuthenticationRequired) return;
                _ = UpdateFavicon(page,id);
                page.Checking = false;
                if (!page.Owner.overlayVisible && ReferenceEquals(page.Owner.activePage,page)) control.Visibility = Visibility.Visible;
                page.Owner.Send(new { type = "browser.state",id,workspace,loading = false,error = e.IsSuccess ? null : e.WebErrorStatus.ToString() });
            };
            core.WindowCloseRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() => { page.Owner.Send(new { type = "browser.closed",id,workspace }); page.Owner.Children.Remove(control); control.Dispose(); page.Owner.pages.Remove(id); }));
            core.NewWindowRequested += async (_, e) =>
            {
                using var deferral = e.GetDeferral(); e.Handled = true;
                if (workspace != page.Owner.Runtime.ContextKey) return;
                try
                {
                    if (e.Uri != "about:blank") StationServices.ValidateWebAddress(e.Uri);
                    if (e.Uri != "about:blank" && StationServices.IsAuthenticationAddress(new Uri(e.Uri))) { page.Owner.OpenExternal(new Uri(e.Uri)); return; }
                    string popupId = Guid.NewGuid().ToString("N"); var popup = await page.Owner.CreatePage(popupId,workspace); popup.RequestedUrl = e.Uri;
                    e.NewWindow = popup.Control.CoreWebView2;
                    page.Owner.Send(new { type = "browser.opened",id = popupId,workspace,title = "New tab",url = e.Uri });
                }
                catch (Exception error) { page.Owner.Send(new { type = "notice",message = error.Message }); }
            };
            return page;
        }
        catch
        {
            if (pending is not null && (pending.IsFaulted || pending.IsCanceled) && environments.TryGetValue(workspace,out var cached) && ReferenceEquals(cached,pending)) environments.Remove(workspace);
            pages.Remove(id); Children.Remove(control); control.Dispose(); throw;
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; Runtime.Dispose(); foreach (var page in pages.Values) { page.AuthCancellation?.Cancel(); page.Control.Dispose(); } pages.Clear(); Browser.Dispose(); ready.TrySetCanceled(); }
    private sealed class RemoteView(StationWebView control,string workspace,StationWebHost owner)
    { public StationWebView Control { get; } = control; public string Workspace { get; } = workspace; public StationWebHost Owner = owner; public MemoryStream? RequestBody; public CancellationTokenSource? AuthCancellation; public bool HandoffActive; public string? Favicon; public int FaviconRevision; public ulong NavigationId; public bool AuthenticationRequired, SharedSession, Checking = true; public string RequestedUrl = "https://example.com/"; }
}
