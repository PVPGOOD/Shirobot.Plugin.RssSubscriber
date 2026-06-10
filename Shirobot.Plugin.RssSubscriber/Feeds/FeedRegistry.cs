using Shirobot.Plugin.RssSubscriber.Storage;

namespace Shirobot.Plugin.RssSubscriber.Feeds;

public sealed class FeedRegistry
{
    private readonly Dictionary<string, FeedSource> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void LoadFrom(IReadOnlyDictionary<string, PersistentFeed> persisted)
    {
        lock (_lock)
        {
            _feeds.Clear();
            foreach (var (id, persistedFeed) in persisted)
            {
                _feeds[id] = new FeedSource
                {
                    Id = id,
                    Url = persistedFeed.Url,
                    DisplayName = persistedFeed.DisplayName,
                    IntervalSeconds = persistedFeed.IntervalSeconds,
                    LastSeenGuids = persistedFeed.LastSeenGuids?.ToList() ?? new List<string>(),
                    LastFetchAt = persistedFeed.LastFetchAt,
                    ConsecutiveFailures = persistedFeed.ConsecutiveFailures,
                    CreatedBy = persistedFeed.CreatedBy,
                    CreatedAt = persistedFeed.CreatedAt == default
                        ? DateTimeOffset.UtcNow
                        : persistedFeed.CreatedAt
                };
            }
        }
    }

    public Dictionary<string, PersistentFeed> Snapshot()
    {
        lock (_lock)
        {
            return _feeds.ToDictionary(
                kv => kv.Key,
                kv => new PersistentFeed
                {
                    Url = kv.Value.Url,
                    DisplayName = kv.Value.DisplayName,
                    IntervalSeconds = kv.Value.IntervalSeconds,
                    LastSeenGuids = kv.Value.LastSeenGuids.ToList(),
                    LastFetchAt = kv.Value.LastFetchAt,
                    ConsecutiveFailures = kv.Value.ConsecutiveFailures,
                    CreatedBy = kv.Value.CreatedBy,
                    CreatedAt = kv.Value.CreatedAt
                });
        }
    }

    public bool TryGet(string id, out FeedSource feed)
    {
        lock (_lock)
        {
            if (_feeds.TryGetValue(id, out var found))
            {
                feed = found;
                return true;
            }

            feed = null!;
            return false;
        }
    }

    public IReadOnlyList<FeedSource> All()
    {
        lock (_lock)
        {
            return _feeds.Values.ToList();
        }
    }

    public FeedSource? FindByUrl(string url)
    {
        lock (_lock)
        {
            return _feeds.Values.FirstOrDefault(f =>
                string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool Exists(string id)
    {
        lock (_lock)
        {
            return _feeds.ContainsKey(id);
        }
    }

    public FeedSource Add(string id, string url, string? createdBy)
    {
        lock (_lock)
        {
            var feed = new FeedSource
            {
                Id = id,
                Url = url,
                CreatedBy = createdBy,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _feeds[id] = feed;
            return feed;
        }
    }

    public bool Remove(string id)
    {
        lock (_lock)
        {
            return _feeds.Remove(id);
        }
    }

    public bool Rename(string oldId, string newId)
    {
        lock (_lock)
        {
            if (!_feeds.TryGetValue(oldId, out var feed) || _feeds.ContainsKey(newId))
            {
                return false;
            }

            _feeds.Remove(oldId);
            feed.Id = newId;
            _feeds[newId] = feed;
            return true;
        }
    }

    public void UpdateAfterFetch(string id, IEnumerable<string> newGuids, int lastSeenCapacity)
    {
        lock (_lock)
        {
            if (!_feeds.TryGetValue(id, out var feed))
            {
                return;
            }

            feed.LastFetchAt = DateTimeOffset.UtcNow;
            feed.ConsecutiveFailures = 0;

            foreach (var guid in newGuids)
            {
                if (!feed.LastSeenGuids.Contains(guid))
                {
                    feed.LastSeenGuids.Add(guid);
                }
            }

            if (lastSeenCapacity > 0 && feed.LastSeenGuids.Count > lastSeenCapacity)
            {
                var overflow = feed.LastSeenGuids.Count - lastSeenCapacity;
                feed.LastSeenGuids.RemoveRange(0, overflow);
            }
        }
    }

    public void RecordFailure(string id)
    {
        lock (_lock)
        {
            if (_feeds.TryGetValue(id, out var feed))
            {
                feed.LastFetchAt = DateTimeOffset.UtcNow;
                feed.ConsecutiveFailures++;
            }
        }
    }

    public void SetInterval(string id, int? intervalSeconds)
    {
        lock (_lock)
        {
            if (_feeds.TryGetValue(id, out var feed))
            {
                feed.IntervalSeconds = intervalSeconds;
            }
        }
    }

    public void SetDisplayName(string id, string? displayName)
    {
        lock (_lock)
        {
            if (_feeds.TryGetValue(id, out var feed))
            {
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    return;
                }

                feed.DisplayName = displayName.Trim();
            }
        }
    }

    public void TouchBaseline(string id, IEnumerable<string> guids, int lastSeenCapacity)
    {
        UpdateAfterFetch(id, guids, lastSeenCapacity);
    }
}
