namespace Shirobot.Plugin.RssSubscriber.Feeds;

public sealed record FeedFetchResult(
    string? FeedTitle,
    IReadOnlyList<FeedItem> Items);
