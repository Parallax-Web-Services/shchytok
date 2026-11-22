using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Parallax.Station;

// RFC 7636 S256 and a one-use IPv4 loopback receiver; no public listener.
internal sealed class BrowserAuthCallback : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback,0);
    internal string Verifier { get; } = Base64(RandomNumberGenerator.GetBytes(32));
    internal string State { get; } = Base64(RandomNumberGenerator.GetBytes(32));
    internal string Challenge => Base64(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
    internal Uri Address { get; }
    internal BrowserAuthCallback() { listener.Start(4); Address = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/station/callback"); }
    internal async Task<string> Receive(CancellationToken cancellation)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var stream = client.GetStream();
            var bytes = new List<byte>(); var buffer = new byte[1];
            try
            {
                while (bytes.Count<16384 && await stream.ReadAsync(buffer,timeout.Token)==1)
                {
                    bytes.Add(buffer[0]);
                    if (bytes.Count>=4 && bytes[^4]==13 && bytes[^3]==10 && bytes[^2]==13 && bytes[^1]==10) break;
                }
                string? code = Parse(Encoding.ASCII.GetString(bytes.ToArray()),Address,State);
                string body = code is null ? "This request could not connect Station. Return to the sign-in page." : "Authorization received. Return to Parallax Station to finish connecting.";
                string status = code is null ? "400 Bad Request" : "200 OK";
                byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; frame-ancestors 'none'\r\nConnection: close\r\n\r\n" + body);
                await stream.WriteAsync(response,timeout.Token);
                if (code is not null) { listener.Stop(); return code; }
            }
            catch (OperationCanceledException) { cancellation.ThrowIfCancellationRequested(); }
            catch (IOException) { }
        }
    }
    internal static string? Parse(string request,Uri expected,string state)
    {
        var lines = request.Split("\r\n"); if(lines.Length<2 || !request.EndsWith("\r\n\r\n",StringComparison.Ordinal)) return null;
        var first = lines[0].Split(' '); if(first.Length!=3 || first[0]!="GET" || first[2]!="HTTP/1.1" || !first[1].StartsWith('/'))return null;
        var host = lines.Where(l=>l.StartsWith("Host:",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(host.Length!=1 || host[0][5..].Trim()!=expected.Authority || !Uri.TryCreate(expected,first[1],out var uri) || uri.GetLeftPart(UriPartial.Path)!=expected.GetLeftPart(UriPartial.Path) || uri.Fragment!="") return null;
        var values = new Dictionary<string,string>();
        foreach(string part in uri.Query.TrimStart('?').Split('&')) { var pair=part.Split('=',2); if(pair.Length!=2 || !values.TryAdd(pair[0],Uri.UnescapeDataString(pair[1])))return null; }
        if(!values.TryGetValue("state",out var actual) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state),Encoding.UTF8.GetBytes(actual)) || !values.TryGetValue("code",out var code) || code.Length is < 32 or > 256 || code.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return null;
        return code;
    }
    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    public void Dispose() => listener.Stop();
}
