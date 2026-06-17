using System.Globalization;
using System.Text;
using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Permissions;
using Shirobot.Plugin.RssSubscriber.Scheduler;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Commands;

public sealed class RssCommandHandler
{
    private readonly IBotContext _bot;
    private readonly Func<RssPluginConfig> _configAccessor;
    private readonly FeedRegistry _feeds;
    private readonly SubscriptionRegistry _subscriptions;
    private readonly RssPollScheduler _scheduler;
    private readonly RssDispatcher _dispatcher;
    private readonly Func<Task> _reloadAsync;
    private readonly Action<RssPluginConfig> _saveConfig;

    public RssCommandHandler(
        IBotContext bot,
        Func<RssPluginConfig> configAccessor,
        FeedRegistry feeds,
        SubscriptionRegistry subscriptions,
        RssPollScheduler scheduler,
        RssDispatcher dispatcher,
        Func<Task> reloadAsync,
        Action<RssPluginConfig> saveConfig)
    {
        _bot = bot;
        _configAccessor = configAccessor;
        _feeds = feeds;
        _subscriptions = subscriptions;
        _scheduler = scheduler;
        _dispatcher = dispatcher;
        _reloadAsync = reloadAsync;
        _saveConfig = saveConfig;
    }

    public async Task HandleGroupAsync(GroupIncomingMessage message)
    {
        var canManage = PermissionResolver.CanManageGroupSubscription(_bot, message);
        var ctx = new CommandContext(
            bot: _bot,
            scope: SubscriberKey.Group(message.Group.GroupId),
            senderId: message.SenderId,
            isAdminScope: canManage,
            isGroup: true,
            groupId: message.Group.GroupId,
            replyAsync: (text, segments) => SendGroupAsync(message.Group.GroupId, false, message.SenderId, text, segments),
            replyMentionAsync: (mention, text, segments) =>
                SendGroupAsync(message.Group.GroupId, mention, message.SenderId, text, segments),
            sendSegmentsAsync: segments => _bot.Message.SendGroupMessageAsync(message.Group.GroupId, segments));

        await DispatchAsync(message.GetPlainText(), ctx);
    }

    public async Task HandleFriendAsync(FriendIncomingMessage message)
    {
        var ctx = new CommandContext(
            bot: _bot,
            scope: SubscriberKey.Friend(message.SenderId),
            senderId: message.SenderId,
            isAdminScope: true, // 私聊一律允许操作自己的订阅
            isGroup: false,
            groupId: null,
            replyAsync: (text, segments) => SendPrivateAsync(message.SenderId, text, segments),
            replyMentionAsync: (_, text, segments) => SendPrivateAsync(message.SenderId, text, segments),
            sendSegmentsAsync: segments => _bot.Message.SendPrivateMessageAsync(message.SenderId, segments));

        await DispatchAsync(message.GetPlainText(), ctx);
    }

    private Task SendGroupAsync(long groupId, bool mention, long mentionUserId, string text, IEnumerable<OutgoingSegment>? extra)
    {
        var segments = new List<OutgoingSegment>();
        if (mention)
        {
            segments.Add(new MentionOutgoingSegment(mentionUserId));
            segments.Add(new TextOutgoingSegment(" " + text));
        }
        else
        {
            segments.Add(new TextOutgoingSegment(text));
        }

        if (extra is not null)
        {
            segments.AddRange(extra);
        }

        return _bot.Message.SendGroupMessageAsync(groupId, segments.ToArray());
    }

    private Task SendPrivateAsync(long userId, string text, IEnumerable<OutgoingSegment>? extra)
    {
        var segments = new List<OutgoingSegment> { new TextOutgoingSegment(text) };
        if (extra is not null)
        {
            segments.AddRange(extra);
        }

        return _bot.Message.SendPrivateMessageAsync(userId, segments.ToArray());
    }

    private async Task DispatchAsync(string raw, CommandContext ctx)
    {
        var args = StripPrefix(raw);
        var tokens = Tokenize(args);

        if (tokens.Length == 0 || string.IsNullOrWhiteSpace(tokens[0]))
        {
            await ShowStatusAsync(ctx);
            return;
        }

        switch (tokens[0].ToLowerInvariant())
        {
            case "help":
            case "?":
            case "用法":
            case "帮助":
                await ctx.ReplyAsync(BuildHelp(), null);
                return;

            case "status":
            case "状态":
                await ShowStatusAsync(ctx);
                return;

            case "list":
                await ListSubscribedAsync(ctx);
                return;

            case "add":
                await AddAsync(tokens, ctx);
                return;

            case "remove":
            case "rm":
            case "del":
                await RemoveAsync(tokens, ctx);
                return;

            case "rename":
                await RenameAsync(tokens, ctx);
                return;

            case "latest":
                await LatestAsync(tokens, ctx);
                return;

            case "test":
                await TestAsync(tokens, ctx);
                return;

            case "interval":
                await IntervalAsync(tokens, ctx);
                return;

            case "reload":
                await ReloadAsync(ctx);
                return;

            case "feeds":
                await FeedsAsync(ctx);
                return;

            case "config":
            case "配置":
                await ConfigAsync(tokens, ctx);
                return;

            default:
                await ctx.ReplyAsync("未知子命令。" + BuildHelp(), null);
                return;
        }
    }

    private async Task ShowStatusAsync(CommandContext ctx)
    {
        var config = _configAccessor();
        var ids = _subscriptions.List(ctx.Scope);

        var builder = new StringBuilder();
        builder.AppendLine("[RSS] status");
        builder.AppendLine($"enabled: {(config.Enabled ? "on" : "off")}");
        builder.AppendLine($"scope: {ctx.Scope.Format()}");
        builder.AppendLine($"default_interval: {config.DefaultIntervalSeconds}s");
        builder.AppendLine($"min_interval: {config.MinIntervalSeconds}s");
        builder.AppendLine($"max_items_per_push: {config.MaxItemsPerPush}");
        builder.AppendLine($"include_image: {(config.IncludeImage ? "on" : "off")}");
        builder.AppendLine($"allow_private_urls: {(config.AllowPrivateUrls ? "on" : "off")}");
        builder.AppendLine($"subscriptions: {ids.Count}");
        if (ids.Count > 0)
        {
            foreach (var id in ids)
            {
                if (_feeds.TryGet(id, out var feed))
                {
                    builder.AppendLine($"  - {id} <- {feed.Url}");
                }
                else
                {
                    builder.AppendLine($"  - {id} (missing)");
                }
            }
        }

        await ctx.ReplyAsync(builder.ToString().TrimEnd(), null);
    }

    private async Task ListSubscribedAsync(CommandContext ctx)
    {
        var ids = _subscriptions.List(ctx.Scope);
        if (ids.Count == 0)
        {
            await ctx.ReplyAsync("[RSS] 当前作用域没有订阅。", null);
            return;
        }

        var builder = new StringBuilder();
        builder.Append("[RSS] 当前订阅 (").Append(ids.Count).AppendLine(")");
        foreach (var id in ids)
        {
            if (_feeds.TryGet(id, out var feed))
            {
                if (!string.IsNullOrWhiteSpace(feed.DisplayName))
                {
                    builder.AppendLine($"- {id}  ({feed.DisplayName})");
                }
                else
                {
                    builder.AppendLine($"- {id}");
                }
                builder.AppendLine($"    {feed.Url}");
            }
            else
            {
                builder.AppendLine($"- {id} (missing)");
            }
        }

        await ctx.ReplyAsync(builder.ToString().TrimEnd(), null);
    }

    private async Task AddAsync(string[] tokens, CommandContext ctx)
    {
        if (!ctx.IsAdminScope)
        {
            await ctx.ReplyMentionAsync(true, "[RSS] 仅群管理员或 Bot 主人可执行 add。", null);
            return;
        }

        // 语法: add <url> [id] 或 add <feed_id>
        if (tokens.Length < 2 || tokens.Length > 3)
        {
            await ctx.ReplyAsync("用法: #rss add <url> [id] 或 #rss add <feed_id>", null);
            return;
        }

        var source = tokens[1].Trim();
        var looksLikeUrl = Uri.TryCreate(source, UriKind.Absolute, out var parsedUri) &&
                           !string.IsNullOrWhiteSpace(parsedUri.Scheme);
        if (!looksLikeUrl)
        {
            if (tokens.Length != 2)
            {
                await ctx.ReplyAsync("用法: #rss add <feed_id>", null);
                return;
            }

            var existingId = FeedIdGenerator.Sanitize(source);
            if (!_feeds.Exists(existingId))
            {
                await ctx.ReplyMentionAsync(true, $"[RSS] 未找到 feed_id={existingId}，请先用 #rss add <url> [id] 创建。", null);
                return;
            }

            var subscribed = _subscriptions.Add(ctx.Scope, existingId);
            _scheduler.PersistSync();
            await ctx.ReplyMentionAsync(true,
                subscribed ? $"[RSS] 已订阅 {existingId}。" : $"[RSS] 当前作用域已订阅过 {existingId}。",
                null);
            return;
        }

        var url = source;
        var explicitId = tokens.Length == 3 ? FeedIdGenerator.Sanitize(tokens[2]) : null;
        var githubDefaultFeedId = default(string?);
        var normalizedFromGitHubRepository = FeedIdGenerator.TryNormalizeGitHubRepositoryUrl(url, out var normalizedUrl, out githubDefaultFeedId);
        if (normalizedFromGitHubRepository)
        {
            url = normalizedUrl;
        }

        var config = _configAccessor();
        if (!UrlSafetyGuard.IsAllowed(url, config.AllowPrivateUrls, out var safetyReason))
        {
            await ctx.ReplyMentionAsync(true, "[RSS] " + safetyReason, null);
            return;
        }

        FeedFetchResult validationResult;
        try
        {
            validationResult = await _scheduler.FetchUrlAsync(url, CancellationToken.None);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] add 验证 RSS 源失败 url={url}: {ex.GetType().Name}: {ex.Message}");
            await ctx.ReplyMentionAsync(true,
                $"[RSS] 该 URL 不是有效的 RSS/Atom 源或当前无法访问，请重新添加有效订阅源。\nURL: {url}\n原因: {ex.Message}",
                null);
            return;
        }

        var existingByUrl = _feeds.FindByUrl(url);
        if (existingByUrl is not null)
        {
            // 同 URL 复用
            if (explicitId is not null && !string.Equals(explicitId, existingByUrl.Id, StringComparison.OrdinalIgnoreCase))
            {
                await ctx.ReplyMentionAsync(true,
                    $"[RSS] 同 URL 已存在 feed_id={existingByUrl.Id}，无法另起 id={explicitId}。",
                    null);
                return;
            }

            var added = _subscriptions.Add(ctx.Scope, existingByUrl.Id);
            if (!string.IsNullOrWhiteSpace(validationResult.FeedTitle))
            {
                _feeds.SetDisplayName(existingByUrl.Id, validationResult.FeedTitle);
            }
            _scheduler.PersistSync();
            var msg = added
                ? $"[RSS] 已订阅已有 feed: {existingByUrl.Id}"
                : $"[RSS] 当前作用域已订阅过 {existingByUrl.Id}。";
            await ctx.ReplyMentionAsync(true, msg, null);
            return;
        }

        string feedId;
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            if (_feeds.Exists(explicitId))
            {
                await ctx.ReplyMentionAsync(true,
                    $"[RSS] feed_id={explicitId} 已被占用，请换一个。",
                    null);
                return;
            }

            feedId = explicitId;
        }
        else
        {
            var baseId = !string.IsNullOrWhiteSpace(githubDefaultFeedId)
                ? githubDefaultFeedId
                : FeedIdGenerator.Derive(url);
            feedId = FeedIdGenerator.EnsureUnique(baseId, id => _feeds.Exists(id));
        }

        _feeds.Add(feedId, url, ctx.Scope.Format());
        if (!string.IsNullOrWhiteSpace(validationResult.FeedTitle))
        {
            _feeds.SetDisplayName(feedId, validationResult.FeedTitle);
        }
        _feeds.UpdateAfterFetch(
            feedId,
            validationResult.Items.Select(i => i.Id).Where(id => !string.IsNullOrWhiteSpace(id)),
            config.LastSeenCapacity);
        _subscriptions.Add(ctx.Scope, feedId);
        _scheduler.PersistSync();

        var normalizeMessage = normalizedFromGitHubRepository
            ? $"\n已自动转换 GitHub 仓库地址为: {url}"
            : string.Empty;
        await ctx.ReplyMentionAsync(true,
            $"[RSS] 已添加并订阅 feed_id={feedId}，已验证 RSS/Atom 源并记录 {validationResult.Items.Count} 条历史，下次新增条目将推送到本会话。{normalizeMessage}",
            null);
    }

    private async Task RemoveAsync(string[] tokens, CommandContext ctx)
    {
        if (!ctx.IsAdminScope)
        {
            await ctx.ReplyMentionAsync(true, "[RSS] 仅群管理员或 Bot 主人可执行 remove。", null);
            return;
        }

        if (tokens.Length < 2)
        {
            await ctx.ReplyAsync("用法: #rss remove <feed_id>", null);
            return;
        }

        var id = FeedIdGenerator.Sanitize(tokens[1]);
        if (!_feeds.Exists(id))
        {
            await ctx.ReplyMentionAsync(true, $"[RSS] 未找到 feed_id={id}。", null);
            return;
        }

        var removed = _subscriptions.Remove(ctx.Scope, id);
        if (!removed)
        {
            await ctx.ReplyMentionAsync(true, $"[RSS] 当前作用域没有订阅 {id}。", null);
            return;
        }

        if (!_subscriptions.HasAnySubscriber(id))
        {
            _feeds.Remove(id);
            BotLog.Info($"[Rss] feed={id} 已无订阅者，自动清理。");
        }

        _scheduler.PersistSync();
        await ctx.ReplyMentionAsync(true, $"[RSS] 已退订 {id}。", null);
    }

    private async Task RenameAsync(string[] tokens, CommandContext ctx)
    {
        if (!PermissionResolver.IsBotSuperAdmin(_bot, ctx.SenderId))
        {
            await ctx.ReplyMentionAsync(true, "[RSS] 仅 Bot 主人可执行 rename。", null);
            return;
        }

        if (tokens.Length < 3)
        {
            await ctx.ReplyAsync("用法: #rss rename <old_feed_id> <new_feed_id>", null);
            return;
        }

        var oldId = FeedIdGenerator.Sanitize(tokens[1]);
        var newId = FeedIdGenerator.Sanitize(tokens[2]);
        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
        {
            await ctx.ReplyAsync("[RSS] 新旧 feed_id 相同，无需重命名。", null);
            return;
        }

        if (!_feeds.Exists(oldId))
        {
            await ctx.ReplyMentionAsync(true, $"[RSS] 未找到 feed_id={oldId}。", null);
            return;
        }

        if (_feeds.Exists(newId))
        {
            await ctx.ReplyMentionAsync(true, $"[RSS] feed_id={newId} 已存在，请换一个。", null);
            return;
        }

        if (!_feeds.Rename(oldId, newId))
        {
            await ctx.ReplyMentionAsync(true, $"[RSS] 重命名失败: {oldId} -> {newId}。", null);
            return;
        }

        var subscriberCount = _subscriptions.RenameFeed(oldId, newId);
        _scheduler.PersistSync();
        await ctx.ReplyAsync($"[RSS] 已重命名 {oldId} -> {newId}，已更新 {subscriberCount} 个订阅关系。", null);
    }

    private async Task LatestAsync(string[] tokens, CommandContext ctx)
    {
        if (tokens.Length < 2)
        {
            await ctx.ReplyAsync("用法: #rss latest <feed_id> [tag] [n=N]", null);
            return;
        }

        var id = FeedIdGenerator.Sanitize(tokens[1]);
        if (!_subscriptions.Contains(ctx.Scope, id))
        {
            await ctx.ReplyAsync($"[RSS] 当前作用域未订阅 {id}，无法查询。", null);
            return;
        }

        if (!_feeds.TryGet(id, out var feed))
        {
            await ctx.ReplyAsync($"[RSS] 未找到 feed_id={id}。", null);
            return;
        }

        var config = _configAccessor();
        string? tag = null;
        var count = 1;

        for (var i = 2; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.StartsWith("n=", StringComparison.OrdinalIgnoreCase) && int.TryParse(token[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedN))
            {
                count = Math.Clamp(parsedN, 1, Math.Max(1, config.LatestMaxN));
            }
            else if (string.IsNullOrEmpty(tag))
            {
                tag = token;
            }
        }

        FeedFetchResult? result;
        try
        {
            result = await _scheduler.FetchOnDemandAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await ctx.ReplyAsync($"[RSS] {id} 抓取失败: {ex.Message}", null);
            return;
        }

        if (result is null)
        {
            await ctx.ReplyAsync($"[RSS] {id} 抓取失败。", null);
            return;
        }

        var items = result.Items;
        var matched = FilterByTag(items, tag);
        if (matched.Count == 0)
        {
            await ctx.ReplyAsync($"[{id}]{(tag is null ? string.Empty : " tag=" + tag)} 暂无匹配条目（已扫描 {items.Count} 条）。", null);
            return;
        }

        var picked = matched
            .OrderByDescending(item => item.Published ?? DateTimeOffset.MinValue)
            .Take(count)
            .ToList();

        // 重新读 feed 以拿到 DisplayName（可能由本次抓取写入）
        if (!_feeds.TryGet(id, out var feedRef))
        {
            await ctx.ReplyAsync($"[RSS] 未找到 feed_id={id}。", null);
            return;
        }

        foreach (var item in picked)
        {
            var segments = await _dispatcher
                .BuildItemSegmentsAsync(feedRef, item, includeImage: true, CancellationToken.None);
            await ctx.SendSegmentsAsync(segments);
        }
    }

    private static List<FeedItem> FilterByTag(IReadOnlyList<FeedItem> items, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return items.ToList();
        }

        var byCategory = items
            .Where(item => item.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (byCategory.Count > 0)
        {
            return byCategory;
        }

        // 回退：模糊匹配 title + description
        return items
            .Where(item =>
                (item.Title?.Contains(tag, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Description?.Contains(tag, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    private async Task TestAsync(string[] tokens, CommandContext ctx)
    {
        if (!ctx.IsAdminScope)
        {
            await ctx.ReplyMentionAsync(true, "[RSS] 仅群管理员或 Bot 主人可执行 test。", null);
            return;
        }

        if (tokens.Length < 2)
        {
            await ctx.ReplyAsync("用法: #rss test <feed_id>", null);
            return;
        }

        var id = FeedIdGenerator.Sanitize(tokens[1]);
        if (!_subscriptions.Contains(ctx.Scope, id))
        {
            await ctx.ReplyAsync($"[RSS] 当前作用域未订阅 {id}，无法 test。", null);
            return;
        }

        if (!_feeds.TryGet(id, out var feed))
        {
            await ctx.ReplyAsync($"[RSS] 未找到 feed_id={id}。", null);
            return;
        }

        var result = await _scheduler.FetchOnDemandAsync(id, CancellationToken.None);
        if (result is null || result.Items.Count == 0)
        {
            await ctx.ReplyAsync($"[RSS] {id} 没有可用条目或抓取失败。", null);
            return;
        }

        var latest = result.Items
            .OrderByDescending(item => item.Published ?? DateTimeOffset.MinValue)
            .First();

        if (!_feeds.TryGet(id, out var refreshed))
        {
            refreshed = feed;
        }

        var segments = await _dispatcher
            .BuildItemSegmentsAsync(refreshed, latest, includeImage: true, CancellationToken.None);
        await ctx.SendSegmentsAsync(segments);
    }

    private async Task IntervalAsync(string[] tokens, CommandContext ctx)
    {
        var config = _configAccessor();
        if (!PermissionResolver.IsBotSuperAdmin(_bot, ctx.SenderId))
        {
            await ctx.ReplyMentionAsync(true, "[RSS] 仅 Bot 主人可执行 interval。", null);
            return;
        }

        if (tokens.Length < 3)
        {
            await ctx.ReplyAsync("用法: #rss interval <feed_id> <seconds>", null);
            return;
        }

        var id = FeedIdGenerator.Sanitize(tokens[1]);
        if (!_feeds.TryGet(id, out var feed))
        {
            await ctx.ReplyAsync($"[RSS] 未找到 feed_id={id}。", null);
            return;
        }

        if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            await ctx.ReplyAsync("[RSS] seconds 必须是正整数。", null);
            return;
        }

        seconds = Math.Max(seconds, config.MinIntervalSeconds);
        _feeds.SetInterval(id, seconds);
        _scheduler.PersistSync();
        await ctx.ReplyAsync($"[RSS] feed {id} 间隔已设为 {seconds}s。", null);
    }

    private async Task ReloadAsync(CommandContext ctx)
    {
        if (!PermissionResolver.IsBotSuperAdmin(_bot, ctx.SenderId))
        {
            await ctx.ReplyMentionAsync(true, "[RSS] 仅 Bot 主人可执行 reload。", null);
            return;
        }

        try
        {
            await _reloadAsync();
            await ctx.ReplyAsync("[RSS] 已重新加载配置与状态。", null);
        }
        catch (Exception ex)
        {
            await ctx.ReplyAsync($"[RSS] reload 失败: {ex.Message}", null);
        }
    }

    private async Task FeedsAsync(CommandContext ctx)
    {
        if (!PermissionResolver.IsBotSuperAdmin(_bot, ctx.SenderId))
        {
            await ctx.ReplyMentionAsync(true, "[RSS] 仅 Bot 主人可执行 feeds。", null);
            return;
        }

        var all = _feeds.All();
        if (all.Count == 0)
        {
            await ctx.ReplyAsync("[RSS] 当前没有 feed。", null);
            return;
        }

        var builder = new StringBuilder();
        builder.Append("[RSS] 全部 feeds (").Append(all.Count).AppendLine(")");
        foreach (var feed in all.OrderBy(f => f.Id, StringComparer.OrdinalIgnoreCase))
        {
            var subs = _subscriptions.SubscribersOf(feed.Id);
            builder.Append("- ").Append(feed.Id).Append(" <- ").AppendLine(feed.Url);
            if (!string.IsNullOrWhiteSpace(feed.DisplayName))
            {
                builder.Append("    title: ").AppendLine(feed.DisplayName);
            }
            builder.Append("    interval=")
                .Append(feed.IntervalSeconds?.ToString(CultureInfo.InvariantCulture) ?? "default")
                .Append(", failures=").Append(feed.ConsecutiveFailures)
                .Append(", subs=").Append(subs.Count)
                .AppendLine();
            if (feed.LastFetchAt.HasValue)
            {
                builder.Append("    last_fetch=").AppendLine(feed.LastFetchAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }

        await ctx.ReplyAsync(builder.ToString().TrimEnd(), null);
    }

    private async Task ConfigAsync(string[] tokens, CommandContext ctx)
    {
        var config = _configAccessor();

        if (tokens.Length < 2 || string.Equals(tokens[1], "show", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.ReplyAsync(BuildConfigText(config), null);
            return;
        }

        if (!PermissionResolver.IsBotSuperAdmin(_bot, ctx.SenderId))
        {
            await ctx.ReplyMentionAsync(true, "[RSS] 仅 Bot 主人可修改配置。", null);
            return;
        }

        if (tokens.Length < 3)
        {
            await ctx.ReplyAsync(BuildConfigHelp(), null);
            return;
        }

        var key = tokens[1].ToLowerInvariant();
        var value = tokens[2];

        if (!TryApplyConfig(config, key, value, out var feedback))
        {
            await ctx.ReplyAsync(feedback, null);
            return;
        }

        _saveConfig(config);
        await ctx.ReplyAsync($"[RSS] {feedback}", null);
    }

    private static bool TryApplyConfig(RssPluginConfig config, string key, string value, out string feedback)
    {
        switch (key)
        {
            case "enabled":
            case "插件":
                if (!TryParseBool(value, out var enabled))
                {
                    feedback = $"[RSS] enabled 仅支持 on/off/true/false。";
                    return false;
                }
                config.Enabled = enabled;
                feedback = $"enabled = {(enabled ? "on" : "off")}";
                return true;

            case "default_interval":
            case "default_interval_seconds":
            case "interval":
                if (!TryParsePositiveInt(value, out var defaultInterval))
                {
                    feedback = $"[RSS] default_interval 必须是正整数。";
                    return false;
                }
                config.DefaultIntervalSeconds = Math.Max(defaultInterval, config.MinIntervalSeconds);
                feedback = $"default_interval_seconds = {config.DefaultIntervalSeconds}";
                return true;

            case "min_interval":
            case "min_interval_seconds":
                if (!TryParsePositiveInt(value, out var minInterval))
                {
                    feedback = $"[RSS] min_interval 必须是正整数。";
                    return false;
                }
                config.MinIntervalSeconds = minInterval;
                feedback = $"min_interval_seconds = {minInterval}";
                return true;

            case "request_timeout":
            case "request_timeout_seconds":
            case "timeout":
                if (!TryParsePositiveInt(value, out var timeout))
                {
                    feedback = $"[RSS] request_timeout 必须是正整数。";
                    return false;
                }
                config.RequestTimeoutSeconds = timeout;
                feedback = $"request_timeout_seconds = {timeout}";
                return true;

            case "max_items_per_push":
            case "max_items":
                if (!TryParsePositiveInt(value, out var maxItems))
                {
                    feedback = $"[RSS] max_items_per_push 必须是正整数。";
                    return false;
                }
                config.MaxItemsPerPush = maxItems;
                feedback = $"max_items_per_push = {maxItems}";
                return true;

            case "max_description_length":
            case "max_desc":
                if (!TryParsePositiveInt(value, out var maxDesc))
                {
                    feedback = $"[RSS] max_description_length 必须是正整数。";
                    return false;
                }
                config.MaxDescriptionLength = maxDesc;
                feedback = $"max_description_length = {maxDesc}";
                return true;

            case "latest_max_n":
                if (!TryParsePositiveInt(value, out var latestMax))
                {
                    feedback = $"[RSS] latest_max_n 必须是正整数。";
                    return false;
                }
                config.LatestMaxN = latestMax;
                feedback = $"latest_max_n = {latestMax}";
                return true;

            case "include_image":
            case "图片":
                if (!TryParseBool(value, out var includeImage))
                {
                    feedback = $"[RSS] include_image 仅支持 on/off/true/false。";
                    return false;
                }
                config.IncludeImage = includeImage;
                feedback = $"include_image = {(includeImage ? "on" : "off")}";
                return true;

            case "allow_private_urls":
            case "private":
                if (!TryParseBool(value, out var allowPrivate))
                {
                    feedback = $"[RSS] allow_private_urls 仅支持 on/off/true/false。";
                    return false;
                }
                config.AllowPrivateUrls = allowPrivate;
                feedback = $"allow_private_urls = {(allowPrivate ? "on" : "off")}";
                return true;

            case "user_agent":
                if (string.IsNullOrWhiteSpace(value))
                {
                    feedback = $"[RSS] user_agent 不能为空。";
                    return false;
                }
                config.UserAgent = value;
                feedback = $"user_agent = {value}";
                return true;

            case "last_seen_capacity":
                if (!TryParsePositiveInt(value, out var capacity))
                {
                    feedback = $"[RSS] last_seen_capacity 必须是正整数。";
                    return false;
                }
                config.LastSeenCapacity = capacity;
                feedback = $"last_seen_capacity = {capacity}";
                return true;

            case "backoff_max":
            case "backoff_max_seconds":
                if (!TryParsePositiveInt(value, out var backoff))
                {
                    feedback = $"[RSS] backoff_max_seconds 必须是正整数。";
                    return false;
                }
                config.BackoffMaxSeconds = backoff;
                feedback = $"backoff_max_seconds = {backoff}";
                return true;

            default:
                feedback = $"[RSS] 未知配置项: {key}\n" + BuildConfigHelp();
                return false;
        }
    }

    private static string BuildConfigText(RssPluginConfig config)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[RSS] config");
        builder.AppendLine($"enabled                = {(config.Enabled ? "on" : "off")}");
        builder.AppendLine($"default_interval_sec   = {config.DefaultIntervalSeconds}");
        builder.AppendLine($"min_interval_sec       = {config.MinIntervalSeconds}");
        builder.AppendLine($"request_timeout_sec    = {config.RequestTimeoutSeconds}");
        builder.AppendLine($"max_items_per_push     = {config.MaxItemsPerPush}");
        builder.AppendLine($"max_description_length = {config.MaxDescriptionLength}");
        builder.AppendLine($"latest_max_n           = {config.LatestMaxN}");
        builder.AppendLine($"include_image          = {(config.IncludeImage ? "on" : "off")}");
        builder.AppendLine($"allow_private_urls     = {(config.AllowPrivateUrls ? "on" : "off")}");
        builder.AppendLine($"user_agent             = {config.UserAgent}");
        builder.AppendLine($"last_seen_capacity     = {config.LastSeenCapacity}");
        builder.Append($"backoff_max_seconds    = {config.BackoffMaxSeconds}");
        return builder.ToString();
    }

    private static string BuildConfigHelp() =>
        "用法 (Bot 主人):\n" +
        "#rss config                      查看当前配置\n" +
        "#rss config show                 查看当前配置\n" +
        "#rss config <key> <value>        修改配置项\n" +
        "可用 key:\n" +
        "  enabled                  on|off\n" +
        "  default_interval         <seconds>\n" +
        "  min_interval             <seconds>\n" +
        "  request_timeout          <seconds>\n" +
        "  max_items_per_push       <n>\n" +
        "  max_description_length   <n>\n" +
        "  latest_max_n             <n>\n" +
        "  include_image            on|off\n" +
        "  allow_private_urls       on|off\n" +
        "  user_agent               <ua>\n" +
        "  last_seen_capacity       <n>\n" +
        "  backoff_max_seconds      <seconds>";

    private static bool TryParseBool(string value, out bool parsed)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "on":
            case "true":
            case "1":
            case "yes":
            case "enable":
            case "enabled":
            case "开启":
            case "打开":
            case "启用":
                parsed = true;
                return true;
            case "off":
            case "false":
            case "0":
            case "no":
            case "disable":
            case "disabled":
            case "关闭":
            case "关":
            case "禁用":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }

    private static bool TryParsePositiveInt(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

    private static string BuildHelp() =>
        "用法:\n" +
        "#rss / #rss status              查看当前作用域状态\n" +
        "#rss help                       查看本帮助\n" +
        "#rss list                       列出当前作用域订阅\n" +
        "#rss add <url> [id]             添加 URL 并订阅（群: 仅群管理员/Bot 主人）\n" +
        "#rss add <feed_id>              订阅已存在的 feed\n" +
        "#rss remove <feed_id>           退订当前作用域\n" +
        "#rss rename <old_id> <new_id>   重命名 feed_id（Bot 主人）\n" +
        "#rss latest <feed_id> [tag] [n=N]  立即拉最新（仅本作用域已订阅）\n" +
        "#rss test <feed_id>             立即拉最新一条到当前会话\n" +
        "#rss config [key] [value]       查看/修改配置（修改: Bot 主人）\n" +
        "#rss interval <feed_id> <sec>   设置 feed 轮询间隔（Bot 主人）\n" +
        "#rss reload                     重新加载配置（Bot 主人）\n" +
        "#rss feeds                      列出全部 feed（Bot 主人）";

    private static string StripPrefix(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.StartsWith("#rss", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[4..].Trim();
        }

        return trimmed;
    }

    private static string[] Tokenize(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return Array.Empty<string>();
        }

        return args.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
