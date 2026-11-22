using System.Net.Http;
using System.Text.RegularExpressions;

namespace Parallax.Station;

// Only transport context is exposed. No request headers, bodies or credentials.
public sealed class ServiceNetworkException : HttpRequestException
{
    public string Category { get; }
    public string Detail { get; }
    public ServiceNetworkException(string host,HttpRequestException error)
        : base(error.HttpRequestError,Describe(host,error),error)
    {
        Category = error.HttpRequestError == HttpRequestError.SecureConnectionError ? "tls" : "network";
        Detail = SafeReason(error.InnerException?.Message ?? error.Message);
    }
    private static string SafeReason(string reason)
    {
        reason=Regex.Replace(reason.Replace('\r',' ').Replace('\n',' '),@"(?i)(https?://)[^/\s@]+@","$1[redacted]@");
        return reason.Length>500?reason[..500]:reason;
    }
    private static string Describe(string host,HttpRequestException error)
    {
        string reason=SafeReason(error.InnerException?.Message ?? error.Message);
        return (error.HttpRequestError==HttpRequestError.SecureConnectionError ? "The secure connection to " : "The connection to ") + host + " failed. " + reason;
    }
}
