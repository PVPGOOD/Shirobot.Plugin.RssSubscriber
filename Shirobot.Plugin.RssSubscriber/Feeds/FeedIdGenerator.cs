using System.Globalization;
using System.Text;

namespace Shirobot.Plugin.RssSubscriber.Feeds;


public static class FeedIdGenerator
{
    private static readonly string[] StripPrefixes =
    {
        "www.", "blog.", "feed.", "feeds.", "rss.", "news.", "m.", "mobile."
    };

    private static readonly HashSet<string> DoubleSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.jp", "co.uk", "co.kr", "co.nz", "co.in",
        "com.cn", "com.tw", "com.hk", "com.au", "com.br", "com.sg",
        "edu.cn", "gov.cn", "org.cn", "net.cn", "ac.cn",
        "ne.jp", "ac.jp", "or.jp", "ac.uk", "gov.uk"
    };

    public static bool TryNormalizeGitHubRepositoryUrl(string url, out string normalizedUrl, out string? defaultFeedId)
    {
        normalizedUrl = url;
        defaultFeedId = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        var owner = segments[0];
        var repository = segments[1];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository) ||
            repository.EndsWith(".atom", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var builder = new UriBuilder(uri.Scheme, uri.Host)
        {
            Path = $"/{owner}/{repository}/commits.atom",
            Query = string.Empty,
            Fragment = string.Empty
        };
        normalizedUrl = builder.Uri.ToString();
        defaultFeedId = Sanitize(owner);
        return true;
    }

    public static string Derive(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Sanitize("feed");
        }

        var host = uri.Host.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host))
        {
            return Sanitize("feed");
        }

        // IP host: 不剥前缀，直接用 host + 端口（如有）作为 id
        if (System.Net.IPAddress.TryParse(host, out _))
        {
            var hostId = host;
            if (!uri.IsDefaultPort)
            {
                hostId = host + "-" + uri.Port;
            }

            return Sanitize(hostId);
        }

        foreach (var prefix in StripPrefixes)
        {
            if (host.StartsWith(prefix, StringComparison.Ordinal) && host.Length > prefix.Length)
            {
                host = host[prefix.Length..];
                break;
            }
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length == 0)
        {
            return Sanitize(host);
        }

        if (labels.Length == 1)
        {
            return Sanitize(labels[0]);
        }

        if (labels.Length >= 3)
        {
            var twoSeg = labels[^2] + "." + labels[^1];
            if (DoubleSuffixes.Contains(twoSeg))
            {
                return Sanitize(labels[^3]);
            }
        }

        return Sanitize(labels[^2]);
    }

    public static string EnsureUnique(string baseId, Func<string, bool> isTaken)
    {
        if (!isTaken(baseId))
        {
            return baseId;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = baseId + "-" + i.ToString(CultureInfo.InvariantCulture);
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        return baseId + "-" + Guid.NewGuid().ToString("N")[..6];
    }

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "feed";
        }

        var lowered = raw.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('-');
        while (result.Contains("--", StringComparison.Ordinal))
        {
            result = result.Replace("--", "-");
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return "feed";
        }

        // 纯数字 id 容易和命令参数冲突，加前缀以保持可读
        if (result.All(static c => c is >= '0' and <= '9'))
        {
            return "ip-" + result;
        }

        return result;
    }
}
