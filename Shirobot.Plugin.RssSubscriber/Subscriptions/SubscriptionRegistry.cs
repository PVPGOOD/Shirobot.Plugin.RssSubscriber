using Shirobot.Plugin.RssSubscriber.Storage;

namespace Shirobot.Plugin.RssSubscriber.Subscriptions;

public sealed class SubscriptionRegistry
{
    private readonly Dictionary<long, HashSet<string>> _groupSubs = new();
    private readonly Dictionary<long, HashSet<string>> _friendSubs = new();
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
                if (long.TryParse(key, out var id))
                {
                    _groupSubs[id] = new HashSet<string>(list ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                }
            }

            foreach (var (key, list) in friendSubs)
            {
                if (long.TryParse(key, out var id))
                {
                    _friendSubs[id] = new HashSet<string>(list ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
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
            var bucket = GetBucket(key, create: true)!;
            return bucket.Add(feedId);
        }
    }

    public bool Remove(SubscriberKey key, string feedId)
    {
        lock (_lock)
        {
            var bucket = GetBucket(key, create: false);
            if (bucket is null)
            {
                return false;
            }

            var removed = bucket.Remove(feedId);
            if (removed && bucket.Count == 0)
            {
                if (key.Scope == SubscriberScope.Group)
                {
                    _groupSubs.Remove(key.TargetId);
                }
                else
                {
                    _friendSubs.Remove(key.TargetId);
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
                    subscribers.Add(SubscriberKey.Group(groupId));
                }
            }

            foreach (var (userId, ids) in _friendSubs)
            {
                if (ids.Contains(feedId))
                {
                    subscribers.Add(SubscriberKey.Friend(userId));
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
        if (dict.TryGetValue(key.TargetId, out var bucket))
        {
            return bucket;
        }

        if (!create)
        {
            return null;
        }

        bucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        dict[key.TargetId] = bucket;
        return bucket;
    }
}
