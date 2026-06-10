using System.Text.Json.Serialization;

namespace Shirobot.Plugin.RssSubscriber.Storage;

public sealed class RssState
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("feeds")]
    public Dictionary<string, PersistentFeed> Feeds { get; set; } = new();

    [JsonPropertyName("groupSubs")]
    public Dictionary<string, List<string>> GroupSubs { get; set; } = new();

    [JsonPropertyName("friendSubs")]
    public Dictionary<string, List<string>> FriendSubs { get; set; } = new();
}

public sealed class PersistentFeed
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("intervalSec")]
    public int? IntervalSeconds { get; set; }

    [JsonPropertyName("lastSeen")]
    public List<string> LastSeenGuids { get; set; } = new();

    [JsonPropertyName("lastFetchAt")]
    public DateTimeOffset? LastFetchAt { get; set; }

    [JsonPropertyName("consecutiveFailures")]
    public int ConsecutiveFailures { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
