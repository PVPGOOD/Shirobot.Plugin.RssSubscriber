using System.Net;
using System.Net.Sockets;

namespace Shirobot.Plugin.RssSubscriber.Feeds;

public static class UrlSafetyGuard
{
    public static bool IsAllowed(string url, bool allowPrivate, out string reason)
    {
        reason = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "URL 不是合法的绝对地址。";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = $"仅支持 http/https，当前 scheme={uri.Scheme}。";
            return false;
        }

        if (allowPrivate)
        {
            return true;
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            reason = "URL 缺少 host。";
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            reason = "拒绝订阅 localhost。";
            return false;
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var direct))
        {
            addresses = new[] { direct };
        }
        else
        {
            try
            {
                addresses = Dns.GetHostAddresses(host);
            }
            catch (Exception ex)
            {
                reason = $"无法解析 host {host}: {ex.Message}";
                return false;
            }
        }

        foreach (var address in addresses)
        {
            if (IsPrivateOrSpecial(address))
            {
                reason = $"拒绝订阅内网/回环地址（{address}）。如确需，请管理员在 config.toml 开启 allow_private_urls。";
                return false;
            }
        }

        return true;
    }

    private static bool IsPrivateOrSpecial(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 0.0.0.0/8
            if (bytes[0] == 0)
            {
                return true;
            }

            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 100.64.0.0/10 (CGNAT)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            {
                return true;
            }

            // 127.0.0.0/8
            if (bytes[0] == 127)
            {
                return true;
            }

            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved
            if (bytes[0] >= 224)
            {
                return true;
            }

            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            var bytes = address.GetAddressBytes();
            // fc00::/7 (Unique Local Address)
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            // ::1
            if (address.Equals(IPAddress.IPv6Loopback))
            {
                return true;
            }

            // ::ffff:0:0/96 (IPv4-mapped) — recurse on the embedded IPv4
            if (address.IsIPv4MappedToIPv6)
            {
                return IsPrivateOrSpecial(address.MapToIPv4());
            }
        }

        return false;
    }
}
