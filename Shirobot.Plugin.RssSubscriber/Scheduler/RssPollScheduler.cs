using System.Diagnostics;
using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Storage;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Scheduler;

public sealed class RssPollScheduler : IDisposable
{
    private readonly FeedRegistry _feeds;
    private readonly SubscriptionRegistry _subscriptions;
    private readonly FeedFetcher _fetcher;
    private readonly RssDispatcher _dispatcher;
    private readonly RssStateStore _stateStore;
    private readonly Func<RssPluginConfig> _configAccessor;
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _deliveryReceipts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _receiptLock = new();
    private readonly HashSet<string> _startupBaselines = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _startupAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _startupLock = new();
    private readonly Dictionary<string, Task> _activeChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _activeChecksLock = new();
    private readonly SemaphoreSlim _persistLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private readonly SemaphoreSlim _fetchLimiter = new(4, 4);

    public RssPollScheduler(
        FeedRegistry feeds,
        SubscriptionRegistry subscriptions,
        FeedFetcher fetcher,
        RssDispatcher dispatcher,
        RssStateStore stateStore,
        Func<RssPluginConfig> configAccessor,
        RssState initialState)
    {
        _feeds = feeds;
        _subscriptions = subscriptions;
        _fetcher = fetcher;
        _dispatcher = dispatcher;
        _stateStore = stateStore;
        _configAccessor = configAccessor;
        LoadDeliveryReceipts(initialState.DeliveryReceipts ?? new());
        ResetStartupBaselines(initialState.Feeds.Keys);
    }

    public void ResetStartupBaselines(IEnumerable<string> feedIds)
    {
        lock (_startupLock)
        {
            _startupBaselines.Clear();
            _startupAttempts.Clear();
            _startupBaselines.UnionWith(feedIds);
        }
    }

    public void LoadDeliveryReceipts(Dictionary<string, Dictionary<string, List<string>>> receipts)
    {
        lock (_receiptLock)
        {
            _deliveryReceipts.Clear();
            foreach (var (feedId, items) in receipts)
                _deliveryReceipts[feedId] = items.ToDictionary(
                    pair => pair.Key, pair => new HashSet<string>(pair.Value ?? [], StringComparer.Ordinal),
                    StringComparer.Ordinal);
        }
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

    public async Task<bool> StopAsync()
    {
        if (_cts is null)
        {
            return true;
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
            catch (TimeoutException)
            {
                BotLog.Warning("[Rss] 调度循环未能在 5 秒内停止。");
                return false;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                BotLog.Error($"[Rss] 停止调度循环时异常: {ex}");
            }
        }

        Task[] active;
        lock (_activeChecksLock)
            active = _activeChecks.Values.ToArray();
        try
        {
            await Task.WhenAll(active).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            lock (_activeChecksLock)
                BotLog.Warning($"[Rss] 停止时仍有 {_activeChecks.Count} 个 feed 检查未结束。");
            return false;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
        return true;
    }

    public void Dispose()
    {
        if (!StopAsync().GetAwaiter().GetResult()) return;
        lock (_activeChecksLock)
        {
            if (_activeChecks.Count == 0)
            {
                _fetchLimiter.Dispose();
                _persistLock.Dispose();
            }
        }
    }

    public void ForgetFeed(string feedId)
    {
        ClearReceipts(feedId);
        CompleteStartupBaseline(feedId);
    }

    public void RenameFeed(string oldId, string newId)
    {
        lock (_receiptLock)
        {
            if (_deliveryReceipts.Remove(oldId, out var receipts))
                _deliveryReceipts[newId] = receipts;
        }
        lock (_startupLock)
        {
            if (_startupBaselines.Remove(oldId)) _startupBaselines.Add(newId);
            if (_startupAttempts.Remove(oldId)) _startupAttempts.Add(newId);
        }
    }

    public Task PrimeBaselineAsync(string feedId, CancellationToken cancellationToken) =>
        FetchOnceAsync(feedId, baselineMode: true, force: true, cancellationToken);

    public Task<FeedFetchResult> FetchUrlAsync(string url, CancellationToken cancellationToken) =>
        _fetcher.FetchAsync(url, cancellationToken);

    public Task<FeedFetchResult?> FetchOnDemandAsync(string feedId, CancellationToken cancellationToken) =>
        FetchOnDemandInternalAsync(feedId, cancellationToken);

    private async Task<FeedFetchResult?> FetchOnDemandInternalAsync(string feedId, CancellationToken cancellationToken)
    {
        if (!_feeds.TryGet(feedId, out var feed))
        {
            return null;
        }

        var elapsed = Stopwatch.StartNew();
        try
        {
            var result = await _fetcher.FetchAsync(feed.Url, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.FeedTitle))
            {
                _feeds.SetDisplayName(feedId, result.FeedTitle);
            }
            if (!string.IsNullOrWhiteSpace(result.FeedImageUrl))
                _feeds.SetFeedImageUrl(feedId, result.FeedImageUrl);
            if (!string.IsNullOrWhiteSpace(result.Generator))
                _feeds.SetGenerator(feedId, result.Generator);
            BotLog.Info($"[Rss] 手动抓取成功 feed={feedId} host={FeedHost(feed.Url)} items={result.Items.Count} elapsed_ms={elapsed.ElapsedMilliseconds}");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 手动抓取失败 feed={feedId} host={FeedHost(feed.Url)} elapsed_ms={elapsed.ElapsedMilliseconds} error={DescribeError(ex)}");
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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    BotLog.Error($"[Rss] 调度器迭代异常: {ex}");
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

    private Task TickAsync(CancellationToken cancellationToken)
    {
        var config = _configAccessor();
        if (!config.Enabled)
        {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        var dueFeeds = _feeds.All()
            .Select(feed =>
            {
                var (baseline, attempted) = StartupStatus(feed.Id);
                return (feed.Id, Baseline: baseline, Force: baseline && !attempted,
                    Due: BackoffPolicy.NextDueAt(feed, config) <= now);
            })
            .Where(feed => feed.Force || feed.Due)
            .ToList();

        if (dueFeeds.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var feed in dueFeeds)
        {
            lock (_activeChecksLock)
            {
                if (_activeChecks.ContainsKey(feed.Id))
                    continue;
                if (feed.Baseline)
                    MarkStartupAttempt(feed.Id);
                _activeChecks[feed.Id] = Task.Run(
                    () => RunFeedCheckAsync(feed.Id, feed.Baseline, feed.Force, cancellationToken));
            }
        }

        return Task.CompletedTask;
    }

    private async Task RunFeedCheckAsync(string feedId, bool baselineMode, bool force, CancellationToken cancellationToken)
    {
        try
        {
            await FetchOnceAsync(feedId, baselineMode, force, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            BotLog.Error($"[Rss] feed={feedId} 检查异常: {ex}");
        }
        finally
        {
            lock (_activeChecksLock)
                _activeChecks.Remove(feedId);
        }
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
        var fetchSlotHeld = true;
        try
        {
            var elapsed = Stopwatch.StartNew();
            var priorFailures = feed.ConsecutiveFailures;
            FeedFetchResult result;
            try
            {
                result = baselineMode || feed.LastFetchAt is null
                    ? await _fetcher.FetchAsync(feed.Url, cancellationToken)
                    : await _fetcher.FetchAsync(feed.Url, feed.ETag, feed.LastModified, cancellationToken);
                if (result.NotModified && (baselineMode || feed.LastFetchAt is null))
                    throw new InvalidOperationException("建立历史基线时收到 HTTP 304，无法读取 feed 内容。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _fetchLimiter.Release();
                fetchSlotHeld = false;
                _feeds.RecordFailure(feedId);
                var retryAt = BackoffPolicy.NextDueAt(feed, config);
                var retryIn = Math.Max(0, (int)Math.Ceiling((retryAt - DateTimeOffset.UtcNow).TotalSeconds));
                BotLog.Warning(
                    $"[Rss] 抓取失败 feed={feedId} host={FeedHost(feed.Url)} " +
                    $"elapsed_ms={elapsed.ElapsedMilliseconds} failures={feed.ConsecutiveFailures} " +
                    $"retry_in_sec={retryIn} retry_at={retryAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz} " +
                    $"error={DescribeError(ex)}");
                await PersistAsync();
                return;
            }

            _fetchLimiter.Release();
            fetchSlotHeld = false;

            if (priorFailures > 0)
            {
                BotLog.Info(
                    $"[Rss] 抓取恢复 feed={feedId} host={FeedHost(feed.Url)} " +
                    $"previous_failures={priorFailures} items={result.Items.Count} " +
                    $"elapsed_ms={elapsed.ElapsedMilliseconds}");
            }

            if (result.NotModified)
            {
                _feeds.SetValidators(feedId, result.ETag ?? feed.ETag, result.LastModified ?? feed.LastModified);
                _feeds.UpdateAfterFetch(feedId, [], config.LastSeenCapacity);
                await PersistAsync();
                return;
            }

            _feeds.SetValidators(feedId, result.ETag, result.LastModified);

            if (!string.IsNullOrWhiteSpace(result.FeedTitle))
            {
                _feeds.SetDisplayName(feedId, result.FeedTitle);
            }
            if (!string.IsNullOrWhiteSpace(result.FeedImageUrl))
                _feeds.SetFeedImageUrl(feedId, result.FeedImageUrl);
            if (!string.IsNullOrWhiteSpace(result.Generator))
                _feeds.SetGenerator(feedId, result.Generator);

            var items = result.Items;
            var newItems = SelectNewItems(feed, items);

            if (baselineMode || feed.LastFetchAt is null && feed.LastSeenGuids.Count == 0)
            {
                var allGuids = FeedRegistry.BaselineGuids(items);
                _feeds.UpdateAfterFetch(feedId, allGuids, config.LastSeenCapacity);
                ClearReceipts(feedId);
                CompleteStartupBaseline(feedId);
                BotLog.Info($"[Rss] baseline 完成 feed={feedId} host={FeedHost(feed.Url)} items={items.Count} elapsed_ms={elapsed.ElapsedMilliseconds}，历史条目不推送。");
                await PersistAsync();
                return;
            }

            if (newItems.Count == 0)
            {
                _feeds.UpdateAfterFetch(feedId, [], config.LastSeenCapacity);
                await PersistAsync();
                return;
            }

            var subscribers = _subscriptions.SubscribersOf(feedId);
            if (subscribers.Count == 0)
            {
                _feeds.UpdateAfterFetch(feedId, newItems.Select(i => i.Id), config.LastSeenCapacity);
                ClearReceipts(feedId);
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

            BotLog.Info(
                $"[Rss] 检测到更新 feed={feedId} items={items.Count} new={newItems.Count} " +
                $"push={pushItems.Count} skipped={newItems.Count - pushItems.Count} " +
                $"subscribers={subscribers.Count} elapsed_ms={elapsed.ElapsedMilliseconds}");

            var deliveredIds = new List<string>();
            foreach (var item in pushItems)
            {
                var deliveredToAll = true;
                foreach (var subscriber in subscribers)
                {
                    var target = subscriber.Format();
                    if (HasReceipt(feedId, item.Id, target)) continue;
                    var delivered = await _dispatcher.PushItemAsync(subscriber, feedForPush, item);
                    if (delivered) AddReceipt(feedId, item.Id, target);
                    else deliveredToAll = false;
                }
                if (deliveredToAll)
                {
                    deliveredIds.Add(item.Id);
                    ClearReceipt(feedId, item.Id);
                }
            }

            // Items omitted by max_items_per_push are intentionally skipped; failed sends remain pending.
            foreach (var skipped in newItems.Except(pushItems))
            {
                deliveredIds.Add(skipped.Id);
                ClearReceipt(feedId, skipped.Id);
            }
            _feeds.UpdateAfterFetch(feedId, deliveredIds, config.LastSeenCapacity);

            var pending = pushItems.Count - deliveredIds.Count(id => pushItems.Any(item => item.Id == id));
            if (pending > 0)
                BotLog.Warning($"[Rss] 推送未全部完成 feed={feedId} pending_items={pending}，下次轮询将重试失败目标。");
            else
                BotLog.Info($"[Rss] 推送完成 feed={feedId} delivered_items={pushItems.Count} subscribers={subscribers.Count}");

            await PersistAsync();
        }
        finally
        {
            if (fetchSlotHeld)
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

    private static string FeedHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(invalid-url)";

    private static string DescribeError(Exception exception)
    {
        var detail = $"{exception.GetType().Name}: {exception.Message}";
        if (exception.InnerException is { } inner)
            detail += $"; inner={inner.GetType().Name}: {inner.Message}";
        return detail.Replace('\r', ' ').Replace('\n', ' ');
    }

    private async Task PersistAsync()
    {
        await _persistLock.WaitAsync();
        try
        {
            await _stateStore.SaveAsync(SnapshotState());
        }
        finally
        {
            _persistLock.Release();
        }
    }

    public void PersistSync()
    {
        _persistLock.Wait();
        try
        {
            _stateStore.SaveSync(SnapshotState());
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private RssState SnapshotState() => new()
    {
        Feeds = _feeds.Snapshot(),
        GroupSubs = _subscriptions.SnapshotGroups(),
        FriendSubs = _subscriptions.SnapshotFriends(),
        DeliveryReceipts = SnapshotReceipts()
    };

    private (bool Baseline, bool Attempted) StartupStatus(string feedId)
    {
        lock (_startupLock)
            return (_startupBaselines.Contains(feedId), _startupAttempts.Contains(feedId));
    }

    private void MarkStartupAttempt(string feedId)
    {
        lock (_startupLock) _startupAttempts.Add(feedId);
    }

    private void CompleteStartupBaseline(string feedId)
    {
        lock (_startupLock)
        {
            _startupBaselines.Remove(feedId);
            _startupAttempts.Remove(feedId);
        }
    }

    private bool HasReceipt(string feedId, string itemId, string target)
    {
        lock (_receiptLock)
            return _deliveryReceipts.TryGetValue(feedId, out var items)
                && items.TryGetValue(itemId, out var targets) && targets.Contains(target);
    }

    private void AddReceipt(string feedId, string itemId, string target)
    {
        lock (_receiptLock)
        {
            if (!_deliveryReceipts.TryGetValue(feedId, out var items))
                _deliveryReceipts[feedId] = items = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (!items.TryGetValue(itemId, out var targets))
                items[itemId] = targets = new HashSet<string>(StringComparer.Ordinal);
            targets.Add(target);
        }
    }

    private void ClearReceipt(string feedId, string itemId)
    {
        lock (_receiptLock)
        {
            if (!_deliveryReceipts.TryGetValue(feedId, out var items)) return;
            items.Remove(itemId);
            if (items.Count == 0) _deliveryReceipts.Remove(feedId);
        }
    }

    private void ClearReceipts(string feedId)
    {
        lock (_receiptLock) _deliveryReceipts.Remove(feedId);
    }

    private Dictionary<string, Dictionary<string, List<string>>> SnapshotReceipts()
    {
        lock (_receiptLock)
            return _deliveryReceipts.ToDictionary(
                feed => feed.Key,
                feed => feed.Value.ToDictionary(item => item.Key, item => item.Value.ToList(), StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);
    }
}
