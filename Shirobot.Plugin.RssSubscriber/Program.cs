using Shirobot.Plugin.RssSubscriber.Commands;
using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Presentation;
using Shirobot.Plugin.RssSubscriber.Presentation.Bilibili;
using Shirobot.Plugin.RssSubscriber.Scheduler;
using Shirobot.Plugin.RssSubscriber.Sources;
using Shirobot.Plugin.RssSubscriber.Storage;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using Shirobot.Plugin.RssSubscriber.Workflows;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

[assembly: ShiroBotApiCompatibility("0.9.2", "0.9.2")]
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.8")]

namespace Shirobot.Plugin.RssSubscriber;

[BotPlugin(
    "Shirobot.Plugin.RssSubscriber",
    Name = "RSS 订阅",
    Version = "0.2.1",
    Description = "RSS / Atom 订阅推送插件，支持群与私聊隔离。",
    Author = "PVPGOOD",
    GithubRepo = "ShirokaProject/Shirobot.Plugin.RssSubscriber",
    Category = PluginCategory.Integration,
    SharedAssemblies = "ShiroBot.Model.QQ")]
public sealed class ShirobotPlugin : PluginBase
{
    private const string CommandPrefix = "#rss";

    private RssPluginConfig _config = new();
    private HttpClient? _httpClient;
    private HttpClient? _feedHttpClient;
    private RssStateStore? _stateStore;
    private FeedRegistry? _feedRegistry;
    private SubscriptionRegistry? _subscriptionRegistry;
    private FeedFetcher? _fetcher;
    private RssDispatcher? _dispatcher;
    private RssPollScheduler? _scheduler;
    private RssCommandHandler? _commandHandler;
    private IDisposable? _configWatcher;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    public override string Name => "RSS 订阅";

    protected override void ConfigureRoutes()
    {
        DirectCommands.MapPrefix(CommandPrefix, HandleDirectAsync);
        GroupCommands.MapPrefix(CommandPrefix, HandleGroupAsync);
        Events.Map<MessageEvent>(message =>
        {
            _dispatcher?.RegisterIncoming(message);
            return Task.CompletedTask;
        });
    }

    protected override Task LoadAsync()
    {
        BotLog.Info("[Rss] 开始初始化。");

        _config = Context.Config.Load<RssPluginConfig>();
        Context.Config.Save(_config);

        var configDirectory = Path.GetDirectoryName(Context.Config.ConfigPath) ?? AppContext.BaseDirectory;
        _stateStore = new RssStateStore(configDirectory);
        var state = _stateStore.Load();

        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _feedHttpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        _feedRegistry = new FeedRegistry();
        _subscriptionRegistry = new SubscriptionRegistry();
        _feedRegistry.LoadFrom(state.Feeds);
        _subscriptionRegistry.LoadFrom(state.GroupSubs, state.FriendSubs);

        _fetcher = new FeedFetcher(_feedHttpClient, () => _config);
        var sources = SourceRegistry.CreateDefault();
        var feedPlatforms = FeedPlatformRegistry.CreateDefault();
        var imageEmbedder = new ImageEmbedder(_httpClient);
        var cardRenderer = new CardRenderer(Context.Render, sources, CardRecipeRegistry.CreateDefault(feedPlatforms),
            new CardMediaLoader(_feedHttpClient, () => _config),
            [new BilibiliVideoDataProvider(_feedHttpClient)], feedPlatforms);
        _dispatcher = new RssDispatcher(Context, () => _config, imageEmbedder, cardRenderer);
        _scheduler = new RssPollScheduler(
            _feedRegistry,
            _subscriptionRegistry,
            _fetcher,
            _dispatcher,
            _stateStore,
            () => _config,
            state);
        var itemWorkflow = new RssItemWorkflow(_scheduler, _feedRegistry, _dispatcher);

        _commandHandler = new RssCommandHandler(
            Context,
            () => _config,
            _feedRegistry,
            _subscriptionRegistry,
            _scheduler,
            itemWorkflow,
            sources,
            ReloadAsync,
            SaveConfig);

        _scheduler.Start();

        try
        {
            _configWatcher = Context.Config.Watch<RssPluginConfig>(updated =>
            {
                _config = updated;
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
        await _reloadLock.WaitAsync();
        try
        {
            try
            {
                _configWatcher?.Dispose();
            }
            catch (Exception ex)
            {
                BotLog.Warning($"[Rss] 停止配置 watch 失败: {ex.GetType().Name}: {ex.Message}");
            }
            _configWatcher = null;
            _commandHandler = null;

            if (_scheduler is not null)
            {
                if (await _scheduler.StopAsync())
                {
                    try
                    {
                        _scheduler.PersistSync();
                    }
                    catch (Exception ex)
                    {
                        BotLog.Error($"[Rss] 卸载时保存状态失败: {ex}");
                    }
                    _scheduler.Dispose();
                    _scheduler = null;
                }
                else
                {
                    BotLog.Warning("[Rss] 有检查任务未结束，本次卸载跳过最终保存和资源释放，避免写入过期状态。");
                    return;
                }
            }

            _httpClient?.Dispose();
            _httpClient = null;
            _feedHttpClient?.Dispose();
            _feedHttpClient = null;
            BotLog.Info("[Rss] 已卸载。");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private Task HandleDirectAsync(MessageEvent message)
    {
        if (!_config.Enabled || _commandHandler is null)
        {
            return Task.CompletedTask;
        }

        return _commandHandler.HandleDirectAsync(message);
    }

    private Task HandleGroupAsync(MessageEvent message)
    {
        if (!_config.Enabled || _commandHandler is null)
        {
            return Task.CompletedTask;
        }

        return _commandHandler.HandleGroupAsync(message);
    }

    private async Task ReloadAsync()
    {
        if (_stateStore is null || _feedRegistry is null || _subscriptionRegistry is null || _scheduler is null)
        {
            return;
        }

        await _reloadLock.WaitAsync();
        try
        {
            if (!await _scheduler.StopAsync())
                throw new TimeoutException("RSS 检查任务仍在运行，稍后再执行 reload。");

            try
            {
                var config = Context.Config.Load<RssPluginConfig>();
                var state = _stateStore.Load();
                _feedRegistry.LoadFrom(state.Feeds);
                _subscriptionRegistry.LoadFrom(state.GroupSubs, state.FriendSubs);
                _scheduler.LoadDeliveryReceipts(state.DeliveryReceipts);
                _scheduler.ResetStartupBaselines(state.Feeds.Keys);
                _config = config;
            }
            finally
            {
                _scheduler.Start();
            }
        }
        finally
        {
            _reloadLock.Release();
        }

        BotLog.Info("[Rss] reload 完成，订阅和运行状态已加载并重新建立基线。");
    }

    private void SaveConfig(RssPluginConfig updated)
    {
        _config = updated;
        Context.Config.Save(_config);
    }
}
