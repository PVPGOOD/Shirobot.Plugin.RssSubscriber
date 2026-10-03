using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Linq;
using Shirobot.Plugin.RssSubscriber.Config;

namespace Shirobot.Plugin.RssSubscriber.Feeds;

public sealed class FeedFetcher
{
    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace ContentNs = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

    private static readonly string[] DateFormats =
    {
        "ddd, dd MMM yyyy HH:mm:ss zzz",
        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
        "ddd, dd MMM yyyy HH:mm zzz",
        "yyyy-MM-ddTHH:mm:sszzz",
        "yyyy-MM-ddTHH:mm:ss.fffzzz",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-dd HH:mm:ss"
    };

    private readonly HttpClient _httpClient;
    private readonly Func<RssPluginConfig> _configAccessor;
    private RssPluginConfig Config => _configAccessor();

    public FeedFetcher(HttpClient httpClient, Func<RssPluginConfig> configAccessor)
    {
        _httpClient = httpClient;
        _configAccessor = configAccessor;
    }

    public Task<FeedFetchResult> FetchAsync(string url, CancellationToken cancellationToken) =>
        FetchAsync(url, null, null, cancellationToken);

    public async Task<FeedFetchResult> FetchAsync(
        string url, string? etag, DateTimeOffset? lastModified, CancellationToken cancellationToken)
    {
        var config = Config;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds, 5, 600)));
        var requestToken = timeout.Token;
        var stage = "URL/DNS 检查";

        try
        {
            var fetched = await SendFollowingRedirectsAsync(
                url, etag, lastModified, config, requestToken, value => stage = value);
            using var response = fetched.Response;
            var finalUrl = fetched.FinalUrl;
            var responseEtag = response.Headers.ETag?.ToString();
            var responseLastModified = response.Content.Headers.LastModified;

            if (response.StatusCode == HttpStatusCode.NotModified)
                return new FeedFetchResult(null, [])
                {
                    NotModified = true,
                    ETag = responseEtag,
                    LastModified = responseLastModified
                };

            stage = "响应读取";
            await using var stream = await response.Content.ReadAsStreamAsync(requestToken);

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CloseInput = false,
                Async = true
            };

            XDocument document;
            try
            {
                stage = "XML 解析";
                using var xmlReader = XmlReader.Create(stream, settings);
                document = await XDocument.LoadAsync(xmlReader, LoadOptions.None, requestToken);
            }
            catch (XmlException ex)
            {
                throw new InvalidOperationException($"解析 RSS/Atom 失败: {ex.Message}", ex);
            }

            var root = document.Root;
            stage = "条目解析";
            if (root is null)
            {
                return new FeedFetchResult(null, Array.Empty<FeedItem>());
            }

            if (string.Equals(root.Name.LocalName, "rss", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(root.Name.LocalName, "RDF", StringComparison.OrdinalIgnoreCase))
            {
                var channel = root
                    .Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "channel", StringComparison.OrdinalIgnoreCase));
                var channelTitle = channel?
                    .Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "title", StringComparison.OrdinalIgnoreCase))
                    ?.Value?.Trim();
                var generator = channel?
                    .Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "generator", StringComparison.OrdinalIgnoreCase))
                    ?.Value?.Trim();
                var imageUrl = channel?
                    .Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "image", StringComparison.OrdinalIgnoreCase))?
                    .Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "url", StringComparison.OrdinalIgnoreCase))?
                    .Value;

                return new FeedFetchResult(NormalizeTitle(channelTitle), ParseRss(root, finalUrl))
                {
                    Generator = generator,
                    FeedImageUrl = ResolveFeedImageUrl(imageUrl, finalUrl),
                    ETag = responseEtag,
                    LastModified = responseLastModified
                };
            }

            if (string.Equals(root.Name.LocalName, "feed", StringComparison.OrdinalIgnoreCase))
            {
                var feedTitle = root.Element(AtomNs + "title")?.Value?.Trim();
                var generator = root.Element(AtomNs + "generator")?.Value?.Trim();
                return new FeedFetchResult(NormalizeTitle(feedTitle), ParseAtom(root, finalUrl))
                {
                    Generator = generator,
                    FeedImageUrl = ResolveFeedImageUrl(
                        root.Element(AtomNs + "icon")?.Value ?? root.Element(AtomNs + "logo")?.Value,
                        finalUrl),
                    ETag = responseEtag,
                    LastModified = responseLastModified
                };
            }

            throw new InvalidOperationException($"未知的 RSS/Atom 根节点: {root.Name}");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"RSS 抓取在{stage}阶段超过 {Math.Clamp(config.RequestTimeoutSeconds, 5, 600)} 秒。", ex);
        }
    }

    private async Task<(HttpResponseMessage Response, string FinalUrl)> SendFollowingRedirectsAsync(
        string url, string? etag, DateTimeOffset? lastModified,
        RssPluginConfig config, CancellationToken cancellationToken, Action<string> setStage)
    {
        var currentUrl = url;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            setStage("URL/DNS 检查");
            var safety = await UrlSafetyGuard.CheckAsync(currentUrl, config.AllowPrivateUrls, cancellationToken);
            if (!safety.Allowed)
                throw new InvalidOperationException(safety.Reason);

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            if (!string.IsNullOrWhiteSpace(config.UserAgent))
                request.Headers.UserAgent.ParseAdd(config.UserAgent);
            request.Headers.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");
            if (EntityTagHeaderValue.TryParse(etag, out var parsedEtag))
                request.Headers.IfNoneMatch.Add(parsedEtag);
            if (lastModified is not null)
                request.Headers.IfModifiedSince = lastModified;

            setStage("HTTP 请求");
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 &&
                response.Headers.Location is { } location)
            {
                response.Dispose();
                if (redirects == 5)
                    throw new InvalidOperationException("RSS 源重定向超过 5 次。");
                currentUrl = new Uri(new Uri(currentUrl), location).ToString();
                continue;
            }

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotModified)
            {
                var error = new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
                response.Dispose();
                throw error;
            }

            return (response, currentUrl);
        }

        throw new InvalidOperationException("RSS 源重定向超过 5 次。");
    }

    private static string? NormalizeTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var stripped = HtmlSanitizer.Strip(raw, 0);
        return string.IsNullOrWhiteSpace(stripped) ? null : stripped;
    }

    private List<FeedItem> ParseRss(XElement root, string baseUrl)
    {
        var items = new List<FeedItem>();
        var itemElements = root
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase));

        foreach (var item in itemElements)
        {
            var titleRaw = ChildValue(item, "title") ?? string.Empty;
            var link = ChildValue(item, "link") ?? string.Empty;
            var guid = ChildValue(item, "guid");
            var pubDateRaw = ChildValue(item, "pubDate") ?? ChildValue(item, "date");
            var dcDate = item.Element(DcNs + "date")?.Value;
            var description = ChildValue(item, "description") ?? string.Empty;
            var contentEncoded = item.Element(ContentNs + "encoded")?.Value;
            var rich = !string.IsNullOrWhiteSpace(contentEncoded) ? contentEncoded! : description;

            var categories = item
                .Elements()
                .Where(e => string.Equals(e.Name.LocalName, "category", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value?.Trim() ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var publishedRaw = pubDateRaw ?? dcDate;
            var published = TryParseDate(publishedRaw);

            var resolvedLink = ResolveUrl(link, baseUrl);
            var id = !string.IsNullOrWhiteSpace(guid)
                ? guid!
                : !string.IsNullOrWhiteSpace(resolvedLink)
                    ? resolvedLink
                    : titleRaw + "|" + (publishedRaw ?? string.Empty);

            var title = HtmlSanitizer.Strip(titleRaw, 0);
            var safeDescription = HtmlSanitizer.Strip(rich, Config.MaxDescriptionLength);
            var fullDescription = HtmlSanitizer.StripFormattedText(rich);
            var firstImage = HtmlSanitizer.FindFirstImage(rich, resolvedLink);

            items.Add(new FeedItem(id, title, resolvedLink, safeDescription, published, categories, firstImage)
            {
                FullDescription = fullDescription
            });
        }

        return items;
    }

    private List<FeedItem> ParseAtom(XElement root, string baseUrl)
    {
        var items = new List<FeedItem>();
        var entries = root.Elements(AtomNs + "entry");
        foreach (var entry in entries)
        {
            var titleRaw = entry.Element(AtomNs + "title")?.Value ?? string.Empty;
            var id = entry.Element(AtomNs + "id")?.Value ?? string.Empty;
            var updated = entry.Element(AtomNs + "updated")?.Value
                          ?? entry.Element(AtomNs + "published")?.Value;
            var summary = entry.Element(AtomNs + "summary")?.Value;
            var content = entry.Element(AtomNs + "content")?.Value;
            var rich = !string.IsNullOrWhiteSpace(content) ? content! : summary ?? string.Empty;

            var link = entry
                .Elements(AtomNs + "link")
                .FirstOrDefault(l => string.Equals((string?)l.Attribute("rel") ?? "alternate", "alternate", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("href")?.Value
                ?? entry.Elements(AtomNs + "link").FirstOrDefault()?.Attribute("href")?.Value
                ?? string.Empty;

            var categories = entry
                .Elements(AtomNs + "category")
                .Select(e => (string?)e.Attribute("term") ?? e.Value)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolvedLink = ResolveUrl(link, baseUrl);
            var published = TryParseDate(updated);
            var effectiveId = !string.IsNullOrWhiteSpace(id)
                ? id
                : !string.IsNullOrWhiteSpace(resolvedLink)
                    ? resolvedLink
                    : titleRaw + "|" + (updated ?? string.Empty);

            var title = HtmlSanitizer.Strip(titleRaw, 0);
            var safeDescription = HtmlSanitizer.Strip(rich, Config.MaxDescriptionLength);
            var fullDescription = HtmlSanitizer.StripFormattedText(rich);
            var firstImage = HtmlSanitizer.FindFirstImage(rich, resolvedLink);

            items.Add(new FeedItem(effectiveId, title, resolvedLink, safeDescription, published, categories, firstImage)
            {
                FullDescription = fullDescription
            });
        }

        return items;
    }

    private static string? ChildValue(XElement parent, string localName)
    {
        var element = parent
            .Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        var value = element?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTimeOffset? TryParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var iso))
        {
            return iso;
        }

        if (DateTimeOffset.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return exact;
        }

        return null;
    }

    private static string ResolveUrl(string url, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseAbsolute) &&
            Uri.TryCreate(baseAbsolute, url, out var combined))
        {
            return combined.ToString();
        }

        return url;
    }

    private static string? ResolveFeedImageUrl(string? value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var resolved = ResolveUrl(value.Trim(), baseUrl);
        return Uri.TryCreate(resolved, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : null;
    }
}
