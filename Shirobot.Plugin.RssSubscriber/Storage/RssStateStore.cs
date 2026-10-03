using System.Text.Json;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Storage;

public sealed class RssStateStore
{
    private const int ReplaceRetryCount = 7;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _statePath;
    private readonly string _subscriptionsPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public RssStateStore(string pluginConfigDirectory)
    {
        _statePath = Path.Combine(pluginConfigDirectory, "state.json");
        _subscriptionsPath = Path.Combine(pluginConfigDirectory, "subscriptions.json");
    }

    public string FilePath => _statePath;
    public string SubscriptionsFilePath => _subscriptionsPath;

    public RssState Load()
    {
        if (!File.Exists(_subscriptionsPath))
        {
            _writeLock.Wait();
            try
            {
                if (!File.Exists(_subscriptionsPath))
                {
                    WriteFile(_subscriptionsPath, new RssSubscriptionsDocument());
                    BotLog.Info("[Rss] subscriptions.json 不存在，已创建空订阅文件，可使用 #rss add 添加订阅。");
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        var subscriptions = ReadFile<RssSubscriptionsDocument>(_subscriptionsPath);
        if (subscriptions.Feeds is null || subscriptions.GroupSubs is null || subscriptions.FriendSubs is null)
            throw new InvalidDataException("subscriptions.json 缺少必要的订阅字段。");

        RssRuntimeDocument runtime;
        try
        {
            runtime = File.Exists(_statePath)
                ? ReadFile<RssRuntimeDocument>(_statePath)
                : new RssRuntimeDocument();
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 加载 state.json 失败，运行状态将重新建立基线，订阅保持不变: {ex.GetType().Name}: {ex.Message}");
            runtime = new RssRuntimeDocument();
        }

        var checkpoints = new Dictionary<string, PersistentFeedCheckpoint>(
            runtime.Feeds ?? new(), StringComparer.OrdinalIgnoreCase);
        var feeds = new Dictionary<string, PersistentFeed>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, definition) in subscriptions.Feeds)
        {
            if (definition is null || string.IsNullOrWhiteSpace(definition.Url))
                throw new InvalidDataException($"subscriptions.json 的 feed={id} 缺少 URL。");

            checkpoints.TryGetValue(id, out var checkpoint);
            feeds[id] = new PersistentFeed
            {
                Url = definition.Url,
                SourceId = definition.SourceId,
                DisplayName = checkpoint?.DisplayName,
                FeedImageUrl = checkpoint?.FeedImageUrl,
                Generator = checkpoint?.Generator,
                IntervalSeconds = definition.IntervalSeconds,
                CreatedBy = definition.CreatedBy,
                CreatedAt = definition.CreatedAt,
                LastSeenGuids = checkpoint?.LastSeenGuids?.ToList() ?? [],
                LastFetchAt = checkpoint?.LastFetchAt,
                ETag = checkpoint?.ETag,
                LastModified = checkpoint?.LastModified,
                ConsecutiveFailures = checkpoint?.ConsecutiveFailures ?? 0
            };
        }

        return new RssState
        {
            Feeds = feeds,
            GroupSubs = subscriptions.GroupSubs,
            FriendSubs = subscriptions.FriendSubs,
            DeliveryReceipts = (runtime.DeliveryReceipts ?? new())
                .Where(receipt => feeds.ContainsKey(receipt.Key))
                .ToDictionary(receipt => receipt.Key, receipt => receipt.Value, StringComparer.OrdinalIgnoreCase)
        };
    }

    public async Task SaveAsync(RssState state, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteFileAsync(_statePath, ToRuntimeDocument(state), cancellationToken);
        }
        catch (Exception ex)
        {
            BotLog.Error($"[Rss] 保存 state.json 失败: {ex}");
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void SaveSync(RssState state)
    {
        _writeLock.Wait();
        try
        {
            WriteFile(_subscriptionsPath, ToSubscriptionsDocument(state));
            WriteFile(_statePath, ToRuntimeDocument(state));
        }
        catch (Exception ex)
        {
            BotLog.Error($"[Rss] 保存订阅或运行状态失败: {ex}");
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static T ReadFile<T>(string path)
    {
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException($"{Path.GetFileName(path)} 为空。");
        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} 无法解析。");
    }

    private static async Task WriteFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tempPath = CreateTempPath(path);
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, WriteOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await ReplaceFileWithRetryAsync(tempPath, path, cancellationToken);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static void WriteFile<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = CreateTempPath(path);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       16 * 1024, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, WriteOptions);
                stream.Flush(flushToDisk: true);
            }

            ReplaceFileWithRetry(tempPath, path);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static string CreateTempPath(string path) =>
        $"{path}.{Guid.NewGuid():N}.tmp";

    private static async Task ReplaceFileWithRetryAsync(
        string tempPath, string destinationPath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ReplaceFile(tempPath, destinationPath);
                return;
            }
            catch (Exception ex) when (IsFileBusy(ex) && attempt < ReplaceRetryCount - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << attempt)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static void ReplaceFileWithRetry(string tempPath, string destinationPath)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                ReplaceFile(tempPath, destinationPath);
                return;
            }
            catch (Exception ex) when (IsFileBusy(ex) && attempt < ReplaceRetryCount - 1)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * (1 << attempt)));
            }
        }
    }

    private static void ReplaceFile(string tempPath, string destinationPath)
    {
        // File.Replace can fail on Windows while removing the destination even after retries
        // (for example, when another process briefly opens state.json). Move with overwrite
        // uses the platform's same-volume replace operation and handles this case more reliably.
        File.Move(tempPath, destinationPath, overwrite: true);
    }

    private static bool IsFileBusy(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (IsFileBusy(ex))
        {
            BotLog.Warning($"[Rss] 清理临时状态文件失败: file={Path.GetFileName(path)} error={ex.GetType().Name}");
        }
    }

    private static RssSubscriptionsDocument ToSubscriptionsDocument(RssState state) => new()
    {
        Feeds = state.Feeds.ToDictionary(
            feed => feed.Key,
            feed => new PersistentFeedDefinition
            {
                Url = feed.Value.Url,
                SourceId = feed.Value.SourceId,
                IntervalSeconds = feed.Value.IntervalSeconds,
                CreatedBy = feed.Value.CreatedBy,
                CreatedAt = feed.Value.CreatedAt
            }, StringComparer.OrdinalIgnoreCase),
        GroupSubs = state.GroupSubs,
        FriendSubs = state.FriendSubs
    };

    private static RssRuntimeDocument ToRuntimeDocument(RssState state) => new()
    {
        Feeds = state.Feeds.ToDictionary(
            feed => feed.Key,
            feed => new PersistentFeedCheckpoint
            {
                DisplayName = feed.Value.DisplayName,
                FeedImageUrl = feed.Value.FeedImageUrl,
                Generator = feed.Value.Generator,
                LastSeenGuids = feed.Value.LastSeenGuids?.ToList() ?? [],
                LastFetchAt = feed.Value.LastFetchAt,
                ETag = feed.Value.ETag,
                LastModified = feed.Value.LastModified,
                ConsecutiveFailures = feed.Value.ConsecutiveFailures
            }, StringComparer.OrdinalIgnoreCase),
        DeliveryReceipts = state.DeliveryReceipts
    };
}
