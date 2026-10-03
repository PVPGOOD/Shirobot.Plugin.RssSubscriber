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
                    SourceId = persistedFeed.SourceId,
                    DisplayName = persistedFeed.DisplayName,
                    FeedImageUrl = persistedFeed.FeedImageUrl,
                    Generator = persistedFeed.Generator,
                    IntervalSeconds = persistedFeed.IntervalSeconds,
                    LastSeenGuids = persistedFeed.LastSeenGuids?.ToList() ?? new List<string>(),
                    LastFetchAt = persistedFeed.LastFetchAt,
                    ETag = persistedFeed.ETag,
                    LastModified = persistedFeed.LastModified,
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
                    SourceId = kv.Value.SourceId,
                    DisplayName = kv.Value.DisplayName,
                    FeedImageUrl = kv.Value.FeedImageUrl,
                    Generator = kv.Value.Generator,
                    IntervalSeconds = kv.Value.IntervalSeconds,
                    LastSeenGuids = kv.Value.LastSeenGuids.ToList(),
                    LastFetchAt = kv.Value.LastFetchAt,
                    ETag = kv.Value.ETag,
                    LastModified = kv.Value.LastModified,
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
                UrlEquals(f.Url, url));
        }
    }

    public bool Exists(string id)
    {
        lock (_lock)
        {
            return _feeds.ContainsKey(id);
        }
    }

    public bool TryAddInitialized(
        string id, string url, string? displayName, string? createdBy,
        IReadOnlyList<FeedItem> baselineItems, int lastSeenCapacity, out FeedSource feed,
        Action<FeedSource>? onAdded = null, string? sourceId = null, string? feedImageUrl = null,
        string? generator = null)
    {
        lock (_lock)
        {
            var existing = _feeds.Values.FirstOrDefault(f =>
                UrlEquals(f.Url, url));
            if (existing is not null)
            {
                feed = existing;
                return false;
            }

            if (_feeds.TryGetValue(id, out existing))
            {
                feed = existing;
                return false;
            }

            var guids = BaselineGuids(baselineItems).ToList();
            if (lastSeenCapacity > 0 && guids.Count > lastSeenCapacity)
                guids = guids.TakeLast(lastSeenCapacity).ToList();

            feed = new FeedSource
            {
                Id = id,
                Url = url,
                SourceId = sourceId,
                DisplayName = displayName,
                FeedImageUrl = feedImageUrl,
                Generator = generator,
                LastSeenGuids = guids,
                LastFetchAt = DateTimeOffset.UtcNow,
                CreatedBy = createdBy,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _feeds[id] = feed;
            try
            {
                onAdded?.Invoke(feed);
            }
            catch
            {
                _feeds.Remove(id);
                throw;
            }
            return true;
        }
    }

    public static IEnumerable<string> BaselineGuids(IReadOnlyList<FeedItem> items) =>
        items.Reverse()
            .OrderBy(item => item.Published ?? DateTimeOffset.MinValue)
            .Select(item => item.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public static bool UrlEquals(string left, string right)
    {
        if (!Uri.TryCreate(left, UriKind.Absolute, out var leftUri) ||
            !Uri.TryCreate(right, UriKind.Absolute, out var rightUri))
            return string.Equals(left, right, StringComparison.Ordinal);

        return string.Equals(leftUri.Scheme, rightUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(leftUri.IdnHost, rightUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            && leftUri.Port == rightUri.Port
            && string.Equals(leftUri.UserInfo, rightUri.UserInfo, StringComparison.Ordinal)
            && string.Equals(leftUri.PathAndQuery, rightUri.PathAndQuery, StringComparison.Ordinal);
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

    public void SetValidators(string id, string? etag, DateTimeOffset? lastModified)
    {
        lock (_lock)
        {
            if (_feeds.TryGetValue(id, out var feed))
            {
                feed.ETag = etag;
                feed.LastModified = lastModified;
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

    public void SetFeedImageUrl(string id, string? feedImageUrl)
    {
        if (!Uri.TryCreate(feedImageUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return;

        lock (_lock)
        {
            if (_feeds.TryGetValue(id, out var feed))
                feed.FeedImageUrl = uri.AbsoluteUri;
        }
    }

    public void SetGenerator(string id, string? generator)
    {
        if (string.IsNullOrWhiteSpace(generator)) return;
        lock (_lock)
        {
            if (_feeds.TryGetValue(id, out var feed))
                feed.Generator = generator.Trim();
        }
    }

    public void TouchBaseline(string id, IEnumerable<string> guids, int lastSeenCapacity)
    {
        UpdateAfterFetch(id, guids, lastSeenCapacity);
    }
}
