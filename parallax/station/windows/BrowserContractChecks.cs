using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Parallax.Station;

internal static class BrowserContractChecks
{
    internal static async Task Run()
    {
        var transport=new TlsWriteFailure();
        using(var api=new SlipstreamClient("https://station-contract.invalid",transport))
        {
            api.Authenticate("synthetic-no-write-token"); bool rejected=false;
            try{await api.WriteAsync("workstreams/73/agent",new WorkspaceIdentity("https://station-contract.invalid",41,11,"Synthetic"),new{prompt="Synthetic no-write proof"});}
            catch(ServiceNetworkException error){rejected=error.Category=="tls" && error.Message.Contains("station-contract.invalid");}
            if(!rejected || transport.Calls!=1)throw new InvalidOperationException("A failed TLS write was retried or lost its diagnostic context.");
        }
        var target=new Uri("https://unravel.tools/projects/17/arrangement?clip=one%20two&view=midi#track-2");
        if(StationLinks.Parse(StationLinks.Create(target)).AbsoluteUri!=target.AbsoluteUri)throw new InvalidOperationException("Browser-to-Station link lost its address.");
        foreach(string invalid in new[]{"parallax-station://open?url=file%3A%2F%2FC%3A%2FWindows%2Ftest","parallax-station://exec?url=https%3A%2F%2Fexample.com","parallax-station://open?url=https%3A%2F%2Fexample.com&command=run"})
        { bool rejected=false;try{StationLinks.Parse(invalid);}catch(ArgumentException){rejected=true;}if(!rejected)throw new InvalidOperationException("A browser activation accepted an unsupported operation."); }
        string channel="Parallax.Station.Check."+Guid.NewGuid().ToString("N");
        using(var stop=new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var arrival=new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            var listening=StationLinks.Listen(uri=>{arrival.TrySetResult(uri);return Task.CompletedTask;},stop.Token,channel);
            if(!await StationLinks.Forward(target,channel) || (await arrival.Task.WaitAsync(stop.Token)).AbsoluteUri!=target.AbsoluteUri)throw new InvalidOperationException("A browser page could not return to the existing Station process.");
            stop.Cancel(); await listening;
        }
        using var callback=new BrowserAuthCallback();
        string expected=Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(callback.Verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');
        if(callback.Challenge!=expected || callback.Verifier.Length!=43 || callback.State.Length!=43 || callback.Address.Host!="127.0.0.1")throw new InvalidOperationException("Browser PKCE binding failed.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accepted=callback.Receive(timeout.Token);
        using var client=new HttpClient(new HttpClientHandler{UseProxy=false});
        string code=new('x',64);
        var denied=await client.GetAsync(callback.Address+"?code="+code+"&state=wrong",timeout.Token);
        if(denied.StatusCode!=HttpStatusCode.BadRequest || accepted.IsCompleted)throw new InvalidOperationException("A callback with the wrong state was accepted.");
        using var wrongHost=new HttpRequestMessage(HttpMethod.Get,callback.Address+"?code="+code+"&state="+callback.State);wrongHost.Headers.Host="untrusted.example";
        if((await client.SendAsync(wrongHost,timeout.Token)).StatusCode!=HttpStatusCode.BadRequest)throw new InvalidOperationException("An unbound callback host was accepted.");
        var response=await client.GetAsync(callback.Address+"?code="+code+"&state="+callback.State,timeout.Token);
        if(!response.IsSuccessStatusCode || await accepted!=code)throw new InvalidOperationException("The valid browser callback did not complete.");
        string duplicate="GET "+callback.Address.AbsolutePath+"?code="+code+"&state="+callback.State+"&state="+callback.State+" HTTP/1.1\r\nHost: "+callback.Address.Authority+"\r\n\r\n";
        if(BrowserAuthCallback.Parse(duplicate,callback.Address,callback.State)!=null)throw new InvalidOperationException("A duplicate callback parameter was accepted.");
    }
    private sealed class TlsWriteFailure : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Calls++;
            throw new HttpRequestException(HttpRequestError.SecureConnectionError,"Synthetic TLS failure.",new System.Security.Authentication.AuthenticationException("Synthetic certificate rejection."));
        }
    }

}
