using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace KeepClip.Infrastructure;

internal sealed class PrivateApiSession
{
    private readonly byte[] _credentialHash;
    private readonly Uri[] _listeners;
    internal string Credential { get; }
    internal bool ServerOnly { get; }
    internal bool AllowRemote { get; }

    private PrivateApiSession(string credential, bool serverOnly, bool allowRemote, Uri[] listeners)
    {
        Credential = credential;
        _credentialHash = SHA256.HashData(Encoding.ASCII.GetBytes(credential));
        ServerOnly = serverOnly;
        AllowRemote = allowRemote;
        _listeners = listeners;
    }

    internal static PrivateApiSession Create(bool serverOnly, string addresses, string? serverCredential = null, bool allowRemote = false)
    {
        var listeners = addresses.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : throw new InvalidOperationException("Nieprawidłowy adres nasłuchiwania KeepClip.")).ToArray();
        if (listeners.Length == 0) throw new InvalidOperationException("Brak adresu nasłuchiwania KeepClip.");
        foreach (var uri in listeners)
        {
            if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Port <= 0)
                throw new InvalidOperationException("Adres nasłuchiwania musi wskazywać origin HTTP lub HTTPS z konkretnym portem.");
            if (uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6) || uri.Host is "0.0.0.0" or "[::]" or "::")
                throw new InvalidOperationException("Podaj konkretny adres nasłuchiwania, bez wildcardów.");
            if (!IsLoopbackHost(uri.Host) && (!serverOnly || !allowRemote || uri.Scheme != "https"))
                throw new InvalidOperationException("Dostęp sieciowy wymaga trybu server-only, KEEPCLIP_ALLOW_REMOTE=1 oraz HTTPS.");
        }
        if (serverOnly && !IsCredential(serverCredential))
            throw new InvalidOperationException("Tryb server-only wymaga KEEPCLIP_API_TOKEN: losowego klucza 32 bajtów zapisanego jako 64 znaki szesnastkowe.");
        return new PrivateApiSession(serverOnly ? serverCredential! : Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), serverOnly, serverOnly && allowRemote, listeners);
    }

    internal bool Authenticate(string authorization)
    {
        if (authorization.Length > 512) return false;
        string? candidate = null;
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) candidate = authorization[7..];
        else if (ServerOnly && authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..]));
                if (decoded.StartsWith("keepclip:", StringComparison.Ordinal)) candidate = decoded[9..];
            }
            catch (FormatException) { }
        }
        return IsCredential(candidate) && CryptographicOperations.FixedTimeEquals(_credentialHash, SHA256.HashData(Encoding.ASCII.GetBytes(candidate!)));
    }

    internal bool AllowsRequest(HttpRequest request)
    {
        var remote = request.HttpContext.Connection.RemoteIpAddress;
        if (remote?.IsIPv4MappedToIPv6 == true) remote = remote.MapToIPv4();
        bool local = IsLoopbackHost(request.Host.Host) && (remote is null || IPAddress.IsLoopback(remote));
        if (!local && (!AllowRemote || !request.IsHttps || !_listeners.Any(uri => LocalAppOrigin.Matches($"{request.Scheme}://{request.Host}", uri.AbsoluteUri)))) return false;

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !LocalAppOrigin.Matches(origin, $"{request.Scheme}://{request.Host}")) return false;
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0 && site is not ("same-origin" or "none")) return false;
        var referer = request.Headers.Referer.ToString();
        return referer.Length == 0 || LocalAppOrigin.Matches(referer, $"{request.Scheme}://{request.Host}");
    }

    private static bool IsCredential(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool IsLoopbackHost(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}