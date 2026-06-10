using Shirobot.Plugin.RssSubscriber.Commands;
using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Scheduler;
using Shirobot.Plugin.RssSubscriber.Storage;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber;

public sealed class ShirobotPlugin : PluginBase
{
    private const string CommandPrefix = "#rss";

    private RssPluginConfig _config = new();
    private HttpClient? _httpClient;
    private RssStateStore? _stateStore;
    private FeedRegistry? _feedRegistry;
    private SubscriptionRegistry? _subscriptionRegistry;
    private FeedFetcher? _fetcher;
    private RssDispatcher? _dispatcher;
    private RssPollScheduler? _scheduler;
    private RssCommandHandler? _commandHandler;
    private IDisposable? _configWatcher;
    private readonly object _reloadLock = new();

    public override string Name => "Shirobot.Plugin.RssSubscriber";

    public override BotComponentMetadata Metadata { get; } = new()
    {
        Name = "Shirobot.Plugin.RssSubscriber",
        Version = "1.0.0",
        Description = "RSS / Atom 订阅推送插件，支持群与私聊隔离。"
    };

    protected override Task LoadAsync()
    {
        BotLog.Info("[Rss] 开始初始化。");

        _config = Context.Config.Load<RssPluginConfig>();
        Context.Config.Save(_config);

        var configDirectory = Path.GetDirectoryName(Context.Config.ConfigPath) ?? AppContext.BaseDirectory;
        _stateStore = new RssStateStore(configDirectory);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.RequestTimeoutSeconds, 5, 600))
        };

        _feedRegistry = new FeedRegistry();
        _subscriptionRegistry = new SubscriptionRegistry();
        var state = _stateStore.Load();
        _feedRegistry.LoadFrom(state.Feeds);
        _subscriptionRegistry.LoadFrom(state.GroupSubs, state.FriendSubs);

        _fetcher = new FeedFetcher(_httpClient, _config);
        var imageEmbedder = new ImageEmbedder(_httpClient);
        _dispatcher = new RssDispatcher(Context, _config, imageEmbedder);
        _scheduler = new RssPollScheduler(
            _feedRegistry,
            _subscriptionRegistry,
            _fetcher,
            _dispatcher,
            _stateStore,
            () => _config);

        _commandHandler = new RssCommandHandler(
            Context,
            () => _config,
            _feedRegistry,
            _subscriptionRegistry,
            _scheduler,
            _dispatcher,
            ReloadAsync,
            SaveConfig);

        FriendCommands.MapPrefix(CommandPrefix, HandleFriendAsync);
        GroupCommands.MapPrefix(CommandPrefix, HandleGroupAsync);

        _scheduler.Start();

        try
        {
            _configWatcher = Context.Config.Watch<RssPluginConfig>(updated =>
            {
                _config = updated;
                if (_httpClient is not null)
                {
                    _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.RequestTimeoutSeconds, 5, 600));
                }

                BotLog.Info("[Rss] 配置已热重载。");
            });
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 注册配置 watch 失败: {ex.GetType().Name}: {ex.Message}");
        }

        BotLog.Success(
            $"[Rss] 初始化完成。feeds={_feedRegistry.All().Count}, " +
            $"groups={state.GroupSubs.Count}, friends={state.FriendSubs.Count}, " +
            $"interval={_config.DefaultIntervalSeconds}s");

        if (!_config.Enabled)
        {
            BotLog.Warning("[Rss] 插件未启用，请在 config.toml 中将 enabled 设为 true。");
        }

        return Task.CompletedTask;
    }

    protected override async Task OnUnloadAsync()
    {
        try
        {
            _configWatcher?.Dispose();
        }
        catch
        {
        }
        _configWatcher = null;

        if (_scheduler is not null)
        {
            try
            {
                _scheduler.PersistSync();
            }
            catch
            {
            }

            await _scheduler.StopAsync();
            _scheduler.Dispose();
            _scheduler = null;
        }

        _httpClient?.Dispose();
        _httpClient = null;
        BotLog.Info("[Rss] 已卸载。");
    }

    private Task HandleFriendAsync(FriendIncomingMessage message)
    {
        if (!_config.Enabled || _commandHandler is null)
        {
            return Task.CompletedTask;
        }

        return _commandHandler.HandleFriendAsync(message);
    }

    private Task HandleGroupAsync(GroupIncomingMessage message)
    {
        if (!_config.Enabled || _commandHandler is null)
        {
            return Task.CompletedTask;
        }

        return _commandHandler.HandleGroupAsync(message);
    }

    private async Task ReloadAsync()
    {
        if (_stateStore is null || _feedRegistry is null || _subscriptionRegistry is null)
        {
            return;
        }

        await Task.Yield();
        lock (_reloadLock)
        {
            _config = Context.Config.Load<RssPluginConfig>();
            if (_httpClient is not null)
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.RequestTimeoutSeconds, 5, 600));
            }

            var state = _stateStore.Load();
            _feedRegistry.LoadFrom(state.Feeds);
            _subscriptionRegistry.LoadFrom(state.GroupSubs, state.FriendSubs);
        }

        BotLog.Info("[Rss] reload 完成。");
    }

    private void SaveConfig(RssPluginConfig updated)
    {
        _config = updated;
        Context.Config.Save(_config);
        if (_httpClient is not null)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.RequestTimeoutSeconds, 5, 600));
        }
    }
}
