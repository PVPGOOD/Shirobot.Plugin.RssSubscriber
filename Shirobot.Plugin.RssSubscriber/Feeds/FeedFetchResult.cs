namespace Shirobot.Plugin.RssSubscriber.Feeds;

public sealed record FeedFetchResult(
    string? FeedTitle,
    IReadOnlyList<FeedItem> Items)
{
    public string? Generator { get; init; }
    public string? FeedImageUrl { get; init; }
    public bool NotModified { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}
