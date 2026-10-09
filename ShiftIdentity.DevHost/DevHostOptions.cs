using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ShiftIdentity.DevHost;

/// <summary>
/// The command line of the DevHost: <c>--port</c> (0 picks a free one), <c>--lan</c> to bind the machine's private
/// LAN address too, so that a phone on the same network can open the pages, <c>--lan-address</c> to choose that
/// address, <c>--tunnel-url</c> for the public HTTPS address of a tunnel to this host's port, and
/// <c>--access-lifetime</c> for the access-token lifetime in seconds (default 900).
/// Run from Visual Studio, the launch profile supplies the rest: without <c>--port</c> the host takes the port of
/// the profile's application URL, and without <c>--tunnel-url</c> the address of the active dev tunnel.
/// </summary>
internal sealed record DevHostOptions(int Port, IPAddress? LanAddress, string? TunnelOrigin, int AccessLifetimeSeconds)
{
    public static DevHostOptions Parse(string[] args)
    {
        int? port = null;
        var lan = false;
        IPAddress? lanAddress = null;
        string? tunnel = null;
        var accessLifetime = 900;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--port": port = int.Parse(Value()); break;
                case "--lan": lan = true; break;
                case "--lan-address": lan = true; lanAddress = IPAddress.Parse(Value()); break;
                case "--tunnel-url": tunnel = Value(); break;
                case "--access-lifetime": accessLifetime = int.Parse(Value()); break;
                default: throw new ArgumentException($"Unknown option {args[i]}. Options: --port, --lan, --lan-address, --tunnel-url, --access-lifetime.");
            }
        }
        port ??= LaunchProfilePort() ?? 0;
        if (port is < 0 or > 65535) throw new ArgumentException("--port must be between 0 and 65535.");
        if (accessLifetime is < 1 or > 900) throw new ArgumentException("--access-lifetime must be between 1 and 900 seconds.");
        if (lan)
        {
            lanAddress ??= FindLanAddress() ?? throw new InvalidOperationException("No private LAN IPv4 address was found. Pass --lan-address.");
            if (!IsPrivate(lanAddress)) throw new ArgumentException("--lan-address must be a private IPv4 address (10/8, 172.16/12 or 192.168/16).");
        }
        // Visual Studio names the active dev tunnel of the starting project in VS_TUNNEL_URL.
        tunnel ??= Environment.GetEnvironmentVariable("VS_TUNNEL_URL");
        return new(port.Value, lanAddress, string.IsNullOrWhiteSpace(tunnel) ? null : TunnelOriginOf(tunnel), accessLifetime);
    }

    // The tunnel's address as an origin: an HTTPS URL with no path, query or credentials.
    private static string TunnelOriginOf(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new ArgumentException("--tunnel-url (or VS_TUNNEL_URL) must be the tunnel's HTTPS address with no path, for example https://abcd1234-5288.euw.devtunnels.ms/.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    // Visual Studio passes the launch profile's applicationUrl in ASPNETCORE_URLS. Only its port is used: the host
    // still binds loopback, and the LAN address only on request.
    private static int? LaunchProfilePort()
    {
        var first = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return first is not null && Uri.TryCreate(first.Replace("://*", "://localhost").Replace("://+", "://localhost"), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttp ? uri.Port : null;
    }

    public static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>A private IPv4 address (RFC 1918), also when it arrives mapped into IPv6.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    private static IPAddress? FindLanAddress() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(x => x.OperationalStatus == OperationalStatus.Up &&
            x.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
        .SelectMany(x => x.GetIPProperties().UnicastAddresses)
        .Select(x => x.Address)
        .FirstOrDefault(IsPrivate);
}
