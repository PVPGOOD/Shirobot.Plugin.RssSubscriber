namespace Shirobot.Plugin.RssSubscriber.Feeds;

public sealed record FeedItem(
    string Id,
    string Title,
    string Link,
    string Description,
    DateTimeOffset? Published,
    IReadOnlyList<string> Tags,
    string? FirstImageUrl);
