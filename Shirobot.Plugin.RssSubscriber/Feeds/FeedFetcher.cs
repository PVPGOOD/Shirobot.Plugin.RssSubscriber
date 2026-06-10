using System.Globalization;
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
    private readonly RssPluginConfig _config;

    public FeedFetcher(HttpClient httpClient, RssPluginConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<FeedFetchResult> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (!UrlSafetyGuard.IsAllowed(url, _config.AllowPrivateUrls, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(_config.UserAgent))
        {
            request.Headers.UserAgent.ParseAdd(_config.UserAgent);
        }

        request.Headers.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false,
            Async = false
        };

        XDocument document;
        try
        {
            using var xmlReader = XmlReader.Create(stream, settings);
            document = XDocument.Load(xmlReader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"解析 RSS/Atom 失败: {ex.Message}", ex);
        }

        var root = document.Root;
        if (root is null)
        {
            return new FeedFetchResult(null, Array.Empty<FeedItem>());
        }

        if (string.Equals(root.Name.LocalName, "rss", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(root.Name.LocalName, "RDF", StringComparison.OrdinalIgnoreCase))
        {
            var channelTitle = root
                .Descendants()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, "channel", StringComparison.OrdinalIgnoreCase))
                ?.Elements()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, "title", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();

            return new FeedFetchResult(NormalizeTitle(channelTitle), ParseRss(root, url));
        }

        if (string.Equals(root.Name.LocalName, "feed", StringComparison.OrdinalIgnoreCase))
        {
            var feedTitle = root.Element(AtomNs + "title")?.Value?.Trim();
            return new FeedFetchResult(NormalizeTitle(feedTitle), ParseAtom(root, url));
        }

        throw new InvalidOperationException($"未知的 RSS/Atom 根节点: {root.Name}");
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
            var safeDescription = HtmlSanitizer.Strip(rich, _config.MaxDescriptionLength);
            var firstImage = HtmlSanitizer.FindFirstImage(rich, resolvedLink);

            items.Add(new FeedItem(id, title, resolvedLink, safeDescription, published, categories, firstImage));
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
            var safeDescription = HtmlSanitizer.Strip(rich, _config.MaxDescriptionLength);
            var firstImage = HtmlSanitizer.FindFirstImage(rich, resolvedLink);

            items.Add(new FeedItem(effectiveId, title, resolvedLink, safeDescription, published, categories, firstImage));
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
}
