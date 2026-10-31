using System.Net;
using System.Net.Sockets;

namespace TranscriptService.Api.Callbacks;

/// <summary>
/// Keeps signed callbacks from being aimed at the API's own network (SSRF). The check runs on the
/// address actually connected to, after DNS resolution, so a public name resolving to a private
/// address is refused too. Deployments whose receivers are internal opt out explicitly: per host
/// (the demo in compose) or, for tests and closed networks, for every private address.
/// </summary>
internal static class CallbackAddressGuard
{
    public static SocketsHttpHandler CreateHandler(bool allowPrivateAddresses, IReadOnlyCollection<string> allowedHosts) => new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var trusted = allowPrivateAddresses || allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            var allowed = addresses.Where(address => trusted || IsPublic(address)).ToArray();
            if (allowed.Length == 0)
            {
                throw new HttpRequestException($"Callback host '{context.DnsEndPoint.Host}' resolves only to private or reserved addresses.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && (first & 0xFE) != 0xFC; // fc00::/7 unique local
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 or 10 => false,
            100 when bytes[1] is >= 64 and <= 127 => false,  // carrier-grade NAT
            169 when bytes[1] == 254 => false,               // link-local, cloud metadata
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 168 => false,
            >= 224 => false,                                 // multicast and reserved
            _ => true,
        };
    }
}
