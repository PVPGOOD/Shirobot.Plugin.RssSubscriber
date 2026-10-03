using Avalonia.Media.Imaging;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Presentation;

/// <summary>Downloads optional card covers. Feed fetching never calls this service.</summary>
public sealed class CardMediaLoader(HttpClient http, Func<RssPluginConfig> configAccessor)
{
    private const int MaxBytes = 4 * 1024 * 1024;
    private const int MaxHtmlBytes = 1024 * 1024;
    private static readonly Regex LinkTagRegex = new("<link\\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MetaTagRegex = new("<meta\\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AttributeRegex = new("([\\w:-]+)\\s*=\\s*([\\\"'])(.*?)\\2", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public async Task<string?> TryResolveSiteAvatarUrlAsync(string? pageUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) || pageUri.Scheme is not ("http" or "https"))
            return null;

        var origin = new UriBuilder(pageUri.Scheme, pageUri.Host, pageUri.IsDefaultPort ? -1 : pageUri.Port).Uri;
        var safe = await UrlSafetyGuard.CheckAsync(origin.AbsoluteUri, configAccessor().AllowPrivateUrls,
            cancellationToken).ConfigureAwait(false);
        if (!safe.Allowed) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, origin);
            request.Headers.TryAddWithoutValidation("User-Agent", configAccessor().UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.5");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is not > MaxHtmlBytes)
            {
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var memory = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (memory.Length + read > MaxHtmlBytes) break;
                    memory.Write(buffer, 0, read);
                }

                var html = Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
                foreach (Match match in LinkTagRegex.Matches(html))
                {
                    var attrs = ReadAttributes(match.Value);
                    if (!attrs.TryGetValue("rel", out var rel) ||
                        !rel.Contains("icon", StringComparison.OrdinalIgnoreCase) ||
                        !attrs.TryGetValue("href", out var href)) continue;
                    if (ResolvePublicImageUrl(origin, href) is { } iconUrl) return iconUrl;
                }

                foreach (Match match in MetaTagRegex.Matches(html))
                {
                    var attrs = ReadAttributes(match.Value);
                    if (!attrs.TryGetValue("property", out var property) ||
                        !property.Equals("og:image", StringComparison.OrdinalIgnoreCase) ||
                        !attrs.TryGetValue("content", out var content)) continue;
                    if (ResolvePublicImageUrl(origin, content) is { } imageUrl) return imageUrl;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 站点头像发现失败 host={origin.Host}: {ex.GetType().Name}: {ex.Message}");
        }

        return new Uri(origin, "/favicon.ico").AbsoluteUri;
    }

    public async Task<Bitmap?> TryLoadAsync(string? url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var current) || current.Scheme is not ("http" or "https"))
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            for (var redirects = 0; redirects <= 3; redirects++)
            {
                var safety = await UrlSafetyGuard.CheckAsync(current.AbsoluteUri,
                    configAccessor().AllowPrivateUrls, timeout.Token).ConfigureAwait(false);
                if (!safety.Allowed) return null;

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.TryAddWithoutValidation("User-Agent", configAccessor().UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*;q=0.8");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 &&
                    response.Headers.Location is { } location)
                {
                    current = new Uri(current, location);
                    continue;
                }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
                    return null;
                if (response.Content.Headers.ContentType?.MediaType is { } mediaType &&
                    !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    return null;

                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var memory = new MemoryStream();
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (memory.Length + read > MaxBytes) return null;
                    memory.Write(buffer, 0, read);
                }
                if (memory.Length == 0) return null;
                memory.Position = 0;
                return new Bitmap(memory);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 卡片封面加载失败 url={current.Host}: {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    private static Dictionary<string, string> ReadAttributes(string tag) =>
        AttributeRegex.Matches(tag).Cast<Match>().ToDictionary(
            match => match.Groups[1].Value,
            match => WebUtility.HtmlDecode(match.Groups[3].Value),
            StringComparer.OrdinalIgnoreCase);

    private static string? ResolvePublicImageUrl(Uri origin, string value)
    {
        if (!Uri.TryCreate(origin, WebUtility.HtmlDecode(value), out var uri) ||
            uri.Scheme is not ("http" or "https")) return null;
        return uri.AbsoluteUri;
    }
}
