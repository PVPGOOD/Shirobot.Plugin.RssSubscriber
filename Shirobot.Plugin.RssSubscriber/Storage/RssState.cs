using System.Text.Json.Serialization;

namespace Shirobot.Plugin.RssSubscriber.Storage;

public sealed class RssState
{
    [JsonPropertyName("feeds")]
    public Dictionary<string, PersistentFeed> Feeds { get; set; } = new();

    [JsonPropertyName("groupSubs")]
    public Dictionary<string, List<string>> GroupSubs { get; set; } = new();

    [JsonPropertyName("friendSubs")]
    public Dictionary<string, List<string>> FriendSubs { get; set; } = new();

    [JsonPropertyName("deliveryReceipts")]
    public Dictionary<string, Dictionary<string, List<string>>> DeliveryReceipts { get; set; } = new();
}

public sealed class PersistentFeed
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("feedImageUrl")]
    public string? FeedImageUrl { get; set; }

    [JsonPropertyName("generator")]
    public string? Generator { get; set; }

    [JsonPropertyName("intervalSec")]
    public int? IntervalSeconds { get; set; }

    [JsonPropertyName("lastSeen")]
    public List<string> LastSeenGuids { get; set; } = new();

    [JsonPropertyName("lastFetchAt")]
    public DateTimeOffset? LastFetchAt { get; set; }

    [JsonPropertyName("etag")]
    public string? ETag { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; set; }

    [JsonPropertyName("consecutiveFailures")]
    public int ConsecutiveFailures { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
