using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Parallax.Station;

public sealed class ServiceAccessException(string message, int statusCode = 403) : InvalidOperationException(message)
{ public int StatusCode { get; } = statusCode; }

public sealed class ServiceRequestException(int statusCode,string message) : InvalidOperationException(message)
{ public int StatusCode { get; } = statusCode; }

public sealed class SlipstreamClient : IDisposable
{
    private readonly HttpClient http;
    private string? token;
    private readonly HttpMessageHandler? testHandler;
    internal SlipstreamClient CloneForWindow() { var clone = new SlipstreamClient(Origin.GetLeftPart(UriPartial.Authority),testHandler); clone.token = token; return clone; }
    public Uri Origin { get; }
    public SlipstreamClient(string origin, HttpMessageHandler? handler = null)
    {
        testHandler = handler;
        var uri = StationServices.ValidateWebAddress(origin);
        if (uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("Enter the service origin, for example https://slipstream.codes.");
        Origin = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        // Never forward device credentials through an HTTP redirect.
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false },handler is null) { BaseAddress = Origin, Timeout = TimeSpan.FromSeconds(35) };
    }
    public void Authenticate(string credential) => token = credential;
    public async Task<string> ExchangeAsync(string code)
    {
        var data = await RequestAsync("auth/exchange", null, new { code = code.Trim(), device_name = "Parallax Station · " + Environment.MachineName }, false);
        string credential = data.GetProperty("token").GetString() ?? throw new InvalidOperationException("Missing device credential.");
        Authenticate(credential); return credential;
    }
    public Task<JsonElement> SignOutAsync() => RequestAsync("auth/logout", null, new { });
    public Task<JsonElement> ReadAsync(string path, WorkspaceIdentity? workspace = null) => RequestAsync(path, workspace, null);
    public Task<JsonElement> WriteAsync(string path, WorkspaceIdentity workspace, object body) => RequestAsync(path, workspace, body);
    private async Task<JsonElement> RequestAsync(string path, WorkspaceIdentity? workspace, object? body, bool authenticated = true)
    {
        if (path.Contains(':') || path.StartsWith('/') || path.Contains("..")) throw new ArgumentException("Invalid service resource.");
        if (workspace is not null && workspace.Origin != Origin.GetLeftPart(UriPartial.Authority)) throw new InvalidOperationException("Workspace belongs to another service.");
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, "api/v1/" + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? throw new InvalidOperationException("Connect to Slipstream first."));
        if (workspace is not null) request.Headers.Add("X-Slipstream-Team", workspace.TeamId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (body is not null) request.Content = JsonContent.Create(body);
        HttpResponseMessage received;
        try { received = await http.SendAsync(request); }
        catch(HttpRequestException error) { throw new ServiceNetworkException(Origin.Host,error); }
        catch(TaskCanceledException error) when(error.InnerException is TimeoutException)
        { throw new ServiceNetworkException(Origin.Host,new HttpRequestException(HttpRequestError.ConnectionError,"The connection timed out.",error)); }
        using var response = received;
        if ((int)response.StatusCode is 401 or 403) throw new ServiceAccessException((int)response.StatusCode == 401 ? "Your SLIPSTREAM session has expired. Reconnect in your default browser." : "You do not have access to this SLIPSTREAM resource. Your connection is still available.", (int)response.StatusCode);
        if (!response.IsSuccessStatusCode) throw new ServiceRequestException((int)response.StatusCode,$"Slipstream returned {(int)response.StatusCode}. Refresh before retrying. Writes are never retried automatically.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
    public void Dispose() => http.Dispose();
}
