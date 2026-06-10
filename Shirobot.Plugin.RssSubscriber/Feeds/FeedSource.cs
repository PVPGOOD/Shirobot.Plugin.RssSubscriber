namespace Shirobot.Plugin.RssSubscriber.Feeds;

public sealed class FeedSource
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int? IntervalSeconds { get; set; }
    public List<string> LastSeenGuids { get; set; } = new();
    public DateTimeOffset? LastFetchAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
