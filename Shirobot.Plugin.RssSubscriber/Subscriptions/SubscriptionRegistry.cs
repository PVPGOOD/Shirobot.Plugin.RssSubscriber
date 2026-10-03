using Shirobot.Plugin.RssSubscriber.Storage;

namespace Shirobot.Plugin.RssSubscriber.Subscriptions;

public sealed class SubscriptionRegistry
{
    private readonly Dictionary<string, HashSet<string>> _groupSubs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _friendSubs = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public void LoadFrom(
        IReadOnlyDictionary<string, List<string>> groupSubs,
        IReadOnlyDictionary<string, List<string>> friendSubs)
    {
        lock (_lock)
        {
            _groupSubs.Clear();
            _friendSubs.Clear();

            foreach (var (key, list) in groupSubs)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _groupSubs[SubscriberKey.FromStorageKey(SubscriberScope.Group, key).StorageKey] =
                        new HashSet<string>(list ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                }
            }

            foreach (var (key, list) in friendSubs)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _friendSubs[SubscriberKey.FromStorageKey(SubscriberScope.Friend, key).StorageKey] =
                        new HashSet<string>(list ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }

    public Dictionary<string, List<string>> SnapshotGroups()
    {
        lock (_lock)
        {
            return _groupSubs.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }

    public Dictionary<string, List<string>> SnapshotFriends()
    {
        lock (_lock)
        {
            return _friendSubs.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }

    public bool Add(SubscriberKey key, string feedId)
    {
        lock (_lock)
        {
            if (key.InstanceId is not null)
            {
                var dict = key.Scope == SubscriberScope.Group ? _groupSubs : _friendSubs;
                if (!dict.ContainsKey(key.StorageKey) && dict.Remove(key.LegacyStorageKey, out var legacyFeeds))
                {
                    var instanceFeeds = new HashSet<string>(legacyFeeds, StringComparer.OrdinalIgnoreCase);
                    dict[key.StorageKey] = instanceFeeds;
                }
            }

            var bucket = GetBucket(key, create: true)!;
            return bucket.Add(feedId);
        }
    }

    public bool Remove(SubscriberKey key, string feedId)
    {
        lock (_lock)
        {
            var dict = key.Scope == SubscriberScope.Group ? _groupSubs : _friendSubs;
            var storageKey = dict.ContainsKey(key.StorageKey) || key.InstanceId is null
                ? key.StorageKey : key.LegacyStorageKey;
            dict.TryGetValue(storageKey, out var bucket);
            if (bucket is null)
            {
                return false;
            }

            var removed = bucket.Remove(feedId);
            if (removed && bucket.Count == 0)
            {
                if (key.Scope == SubscriberScope.Group)
                {
                    _groupSubs.Remove(storageKey);
                }
                else
                {
                    _friendSubs.Remove(storageKey);
                }
            }

            return removed;
        }
    }

    public bool Contains(SubscriberKey key, string feedId)
    {
        lock (_lock)
        {
            var bucket = GetBucket(key, create: false);
            return bucket is not null && bucket.Contains(feedId);
        }
    }

    public IReadOnlyList<string> List(SubscriberKey key)
    {
        lock (_lock)
        {
            var bucket = GetBucket(key, create: false);
            if (bucket is null)
            {
                return Array.Empty<string>();
            }

            return bucket.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public IReadOnlyList<SubscriberKey> SubscribersOf(string feedId)
    {
        lock (_lock)
        {
            var subscribers = new List<SubscriberKey>();
            foreach (var (groupId, ids) in _groupSubs)
            {
                if (ids.Contains(feedId))
                {
                    subscribers.Add(SubscriberKey.FromStorageKey(SubscriberScope.Group, groupId));
                }
            }

            foreach (var (userId, ids) in _friendSubs)
            {
                if (ids.Contains(feedId))
                {
                    subscribers.Add(SubscriberKey.FromStorageKey(SubscriberScope.Friend, userId));
                }
            }

            return subscribers;
        }
    }

    public int RemoveFeedFromAll(string feedId)
    {
        lock (_lock)
        {
            var removed = 0;
            foreach (var bucket in _groupSubs.Values)
            {
                if (bucket.Remove(feedId))
                {
                    removed++;
                }
            }

            foreach (var bucket in _friendSubs.Values)
            {
                if (bucket.Remove(feedId))
                {
                    removed++;
                }
            }

            CleanupEmptyBuckets();
            return removed;
        }
    }

    public int RenameFeed(string oldId, string newId)
    {
        lock (_lock)
        {
            var changed = 0;
            foreach (var bucket in _groupSubs.Values)
            {
                if (bucket.Remove(oldId))
                {
                    bucket.Add(newId);
                    changed++;
                }
            }

            foreach (var bucket in _friendSubs.Values)
            {
                if (bucket.Remove(oldId))
                {
                    bucket.Add(newId);
                    changed++;
                }
            }

            return changed;
        }
    }

    public bool HasAnySubscriber(string feedId)
    {
        lock (_lock)
        {
            return _groupSubs.Values.Any(set => set.Contains(feedId)) ||
                   _friendSubs.Values.Any(set => set.Contains(feedId));
        }
    }

    private void CleanupEmptyBuckets()
    {
        var emptyGroups = _groupSubs.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
        foreach (var key in emptyGroups)
        {
            _groupSubs.Remove(key);
        }

        var emptyFriends = _friendSubs.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
        foreach (var key in emptyFriends)
        {
            _friendSubs.Remove(key);
        }
    }

    private HashSet<string>? GetBucket(SubscriberKey key, bool create)
    {
        var dict = key.Scope == SubscriberScope.Group ? _groupSubs : _friendSubs;
        if (dict.TryGetValue(key.StorageKey, out var bucket))
        {
            return bucket;
        }

        // Existing subscriptions used platform-only keys. They remain visible to
        // commands after upgrade, and are routed by the dispatcher only when the
        // host has a single matching adapter instance.
        if (!create && key.InstanceId is not null && dict.TryGetValue(key.LegacyStorageKey, out bucket))
            return bucket;

        if (!create)
        {
            return null;
        }

        bucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        dict[key.StorageKey] = bucket;
        return bucket;
    }
}
