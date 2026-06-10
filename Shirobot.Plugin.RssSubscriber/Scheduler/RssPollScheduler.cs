using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Storage;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.SDK.Abstractions;

namespace Shirobot.Plugin.RssSubscriber.Scheduler;

public sealed class RssPollScheduler : IDisposable
{
    private readonly FeedRegistry _feeds;
    private readonly SubscriptionRegistry _subscriptions;
    private readonly FeedFetcher _fetcher;
    private readonly RssDispatcher _dispatcher;
    private readonly RssStateStore _stateStore;
    private readonly Func<RssPluginConfig> _configAccessor;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private readonly SemaphoreSlim _fetchLimiter = new(4, 4);

    public RssPollScheduler(
        FeedRegistry feeds,
        SubscriptionRegistry subscriptions,
        FeedFetcher fetcher,
        RssDispatcher dispatcher,
        RssStateStore stateStore,
        Func<RssPluginConfig> configAccessor)
    {
        _feeds = feeds;
        _subscriptions = subscriptions;
        _fetcher = fetcher;
        _dispatcher = dispatcher;
        _stateStore = stateStore;
        _configAccessor = configAccessor;
    }

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loopTask = Task.Run(() => MainLoopAsync(token), token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _fetchLimiter.Dispose();
    }

    public Task PrimeBaselineAsync(string feedId, CancellationToken cancellationToken) =>
        FetchOnceAsync(feedId, baselineMode: true, force: true, cancellationToken);

    public Task<FeedFetchResult?> FetchOnDemandAsync(string feedId, CancellationToken cancellationToken) =>
        FetchOnDemandInternalAsync(feedId, cancellationToken);

    private async Task<FeedFetchResult?> FetchOnDemandInternalAsync(string feedId, CancellationToken cancellationToken)
    {
        if (!_feeds.TryGet(feedId, out var feed))
        {
            return null;
        }

        try
        {
            var result = await _fetcher.FetchAsync(feed.Url, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.FeedTitle))
            {
                _feeds.SetDisplayName(feedId, result.FeedTitle);
            }
            return result;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] on-demand 抓取 feed={feedId} 失败: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task MainLoopAsync(CancellationToken cancellationToken)
    {
        BotLog.Info("[Rss] 调度器已启动。");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    BotLog.Warning($"[Rss] 调度器迭代异常: {ex.GetType().Name}: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            BotLog.Info("[Rss] 调度器已停止。");
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var config = _configAccessor();
        if (!config.Enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var dueFeeds = _feeds.All()
            .Where(feed => BackoffPolicy.NextDueAt(feed, config) <= now)
            .Select(feed => feed.Id)
            .ToList();

        if (dueFeeds.Count == 0)
        {
            return;
        }

        var tasks = dueFeeds.Select(id => FetchOnceAsync(id, baselineMode: false, force: false, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task FetchOnceAsync(string feedId, bool baselineMode, bool force, CancellationToken cancellationToken)
    {
        var config = _configAccessor();
        if (!_feeds.TryGet(feedId, out var feed))
        {
            return;
        }

        if (!force && feed.LastFetchAt is not null &&
            BackoffPolicy.NextDueAt(feed, config) > DateTimeOffset.UtcNow)
        {
            return;
        }

        await _fetchLimiter.WaitAsync(cancellationToken);
        try
        {
            FeedFetchResult result;
            try
            {
                result = await _fetcher.FetchAsync(feed.Url, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _feeds.RecordFailure(feedId);
                BotLog.Warning($"[Rss] feed={feedId} 抓取失败({feed.ConsecutiveFailures + 1}次): {ex.GetType().Name}: {ex.Message}");
                await PersistAsync();
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.FeedTitle))
            {
                _feeds.SetDisplayName(feedId, result.FeedTitle);
            }

            var items = result.Items;
            var newItems = SelectNewItems(feed, items);

            if (baselineMode || feed.LastFetchAt is null && feed.LastSeenGuids.Count == 0)
            {
                var allGuids = items.Select(i => i.Id).Where(g => !string.IsNullOrWhiteSpace(g));
                _feeds.UpdateAfterFetch(feedId, allGuids, config.LastSeenCapacity);
                BotLog.Info($"[Rss] feed={feedId} baseline 完成，已记录 {items.Count} 条历史，不推送。");
                await PersistAsync();
                return;
            }

            _feeds.UpdateAfterFetch(feedId, newItems.Select(i => i.Id), config.LastSeenCapacity);

            if (newItems.Count == 0)
            {
                await PersistAsync();
                return;
            }

            var subscribers = _subscriptions.SubscribersOf(feedId);
            if (subscribers.Count == 0)
            {
                await PersistAsync();
                return;
            }

            // re-read feed snapshot so that DisplayName from this fetch is included for push
            _feeds.TryGet(feedId, out var refreshed);
            var feedForPush = refreshed ?? feed;

            var pushItems = newItems
                .OrderByDescending(item => item.Published ?? DateTimeOffset.MinValue)
                .Take(Math.Max(1, config.MaxItemsPerPush))
                .Reverse()
                .ToList();

            foreach (var item in pushItems)
            {
                foreach (var subscriber in subscribers)
                {
                    await _dispatcher.PushItemAsync(subscriber, feedForPush, item);
                }
            }

            await PersistAsync();
        }
        finally
        {
            _fetchLimiter.Release();
        }
    }

    private static List<FeedItem> SelectNewItems(FeedSource feed, IReadOnlyList<FeedItem> fetched)
    {
        var seen = new HashSet<string>(feed.LastSeenGuids, StringComparer.OrdinalIgnoreCase);
        var newOnes = new List<FeedItem>();
        foreach (var item in fetched)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                continue;
            }

            if (seen.Add(item.Id))
            {
                newOnes.Add(item);
            }
        }

        return newOnes;
    }

    private async Task PersistAsync()
    {
        var state = new RssState
        {
            Version = 1,
            Feeds = _feeds.Snapshot(),
            GroupSubs = _subscriptions.SnapshotGroups(),
            FriendSubs = _subscriptions.SnapshotFriends()
        };
        await _stateStore.SaveAsync(state);
    }

    public void PersistSync()
    {
        var state = new RssState
        {
            Version = 1,
            Feeds = _feeds.Snapshot(),
            GroupSubs = _subscriptions.SnapshotGroups(),
            FriendSubs = _subscriptions.SnapshotFriends()
        };
        _stateStore.SaveSync(state);
    }
}
