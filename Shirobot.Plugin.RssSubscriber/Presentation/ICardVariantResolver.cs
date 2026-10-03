using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Sources;
using Shirobot.Plugin.RssSubscriber.Presentation.Git;

namespace Shirobot.Plugin.RssSubscriber.Presentation;

public interface ICardVariantResolver
{
    string SourceId { get; }
    string Resolve(FeedSource feed, FeedItem item);
}

public sealed class BilibiliCardVariantResolver : ICardVariantResolver
{
    public string SourceId => "bilibili";

    public string Resolve(FeedSource feed, FeedItem item)
    {
        if (HasPath(item.Link, "/video/")) return "video";
        if (HasPath(item.Link, "/opus/") || HasPath(item.Link, "/dynamic/") ||
            Uri.TryCreate(item.Link, UriKind.Absolute, out var itemUri) &&
            SourceRegistry.HostIs(itemUri, "t.bilibili.com")) return "dynamic";
        if (HasPath(item.Link, "/read/")) return "article";
        if (HasPath(feed.Url, "/bilibili/user/video/")) return "video";
        if (HasPath(feed.Url, "/bilibili/user/dynamic/")) return "dynamic";
        return "article";
    }

    private static bool HasPath(string url, string part) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath.Contains(part, StringComparison.OrdinalIgnoreCase);
}

public sealed class XCardVariantResolver : ICardVariantResolver
{
    public string SourceId => "x";
    public string Resolve(FeedSource feed, FeedItem item) => "post";
}

public sealed class GitCardVariantResolver(string sourceId) : ICardVariantResolver
{
    public string SourceId { get; } = sourceId;
    public string Resolve(FeedSource feed, FeedItem item) => GitCardInfo.ResolveVariant(feed, item);
}
