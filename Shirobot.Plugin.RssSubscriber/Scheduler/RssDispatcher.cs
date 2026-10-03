using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Presentation;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.Model.QQ;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Scheduler;

public sealed class RssDispatcher
{
    public const string Separator = "────────";

    private readonly IBotContext _context;
    private readonly Func<RssPluginConfig> _configAccessor;
    private readonly ImageEmbedder _imageEmbedder;
    private readonly CardRenderer? _cardRenderer;
    private readonly Dictionary<SubscriberKey, (string MessageId, DateTimeOffset SeenAt)> _recent = [];
    private readonly Queue<SubscriberKey> _recentOrder = [];

    public RssDispatcher(IBotContext context, Func<RssPluginConfig> configAccessor, ImageEmbedder imageEmbedder,
        CardRenderer? cardRenderer = null)
    {
        _context = context;
        _configAccessor = configAccessor;
        _imageEmbedder = imageEmbedder;
        _cardRenderer = cardRenderer;
    }

    public void RegisterIncoming(MessageEvent message)
    {
        var key = message.IsDirect
            ? SubscriberKey.Friend(message.Platform, message.Channel.Id, message.InstanceId)
            : SubscriberKey.Group(message.Platform, message.Channel.Id, message.InstanceId);
        lock (_recent)
        {
            if (!_recent.ContainsKey(key)) _recentOrder.Enqueue(key);
            _recent[key] = (message.MessageId, DateTimeOffset.UtcNow);
            while (_recentOrder.Count > 4096) _recent.Remove(_recentOrder.Dequeue());
        }
    }

    public async Task<bool> PushItemAsync(SubscriberKey subscriber, FeedSource feed, FeedItem item)
    {
        var replyTo = RecentMessageId(subscriber);
        return await SendItemAsync(subscriber, feed, item, replyTo, _configAccessor().IncludeImage).ConfigureAwait(false);
    }

    public async Task<bool> SendItemAsync(
        SubscriberKey subscriber, FeedSource feed, FeedItem item, string? replyToMessageId, bool includeImage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var instanceId = subscriber.InstanceId;
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                var matches = _context.GetAdapterInstances()
                    .Where(instance => string.Equals(instance.Platform, subscriber.Platform,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(matches.Length == 0
                        ? $"未找到平台 {subscriber.Platform} 的适配器实例。"
                        : $"旧订阅未记录适配器实例，平台 {subscriber.Platform} 当前有 {matches.Length} 个实例；请在目标实例中重新订阅。");
                instanceId = matches[0].Id;
            }
            using var instanceScope = _context.UseInstance(instanceId);
            var channel = subscriber.Scope == SubscriberScope.Group
                ? Channel.Group(subscriber.TargetId) : Channel.Direct(subscriber.TargetId);
            var official = _context.GetAdapterExtension<IQOfficialMessageApi>();
            var config = _configAccessor();
            if (string.Equals(subscriber.Platform, "qq-official", StringComparison.OrdinalIgnoreCase) &&
                config.EnableRenderedCards && _cardRenderer is not null)
            {
                var mediaApi = _context.GetAdapterExtension<IQOfficialMediaApi>();
                if (mediaApi is null || official is null)
                {
                    BotLog.Warning("[Rss] QQ 官方适配器未暴露统一消息与媒体发送能力，卡片发送回退 Markdown。");
                }
                else
                {
                    var target = new QOfficialMessageTarget(
                        subscriber.Scope == SubscriberScope.Group
                            ? QOfficialMessageScene.Group
                            : QOfficialMessageScene.Direct,
                        subscriber.TargetId);
                    var png = await _cardRenderer.TryRenderAsync(feed, item, includeImage, cancellationToken)
                        .ConfigureAwait(false);
                    if (png is { Length: > 0 })
                    {
                        var imageSent = false;
                        var captionSent = false;
                        var published = item.Published?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未知";
                        var caption = _cardRenderer.RendersFeedMetadataInCard(feed)
                            ? null
                            : $"来源: {_cardRenderer.ResolveSourceName(feed, item)}\n发布时间: {published}";
                        try
                        {
                            await using var image = new MemoryStream(png, writable: false);
                            var reply = string.IsNullOrWhiteSpace(replyToMessageId)
                                ? null
                                : new QOfficialMessageReply { MessageId = replyToMessageId };
                            if (string.IsNullOrWhiteSpace(caption))
                            {
                                await official.SendAsync(target,
                                    QOfficialMessage.Media(QOfficialMediaType.Image, image, $"rss-{feed.Id}.png"),
                                    reply, cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                try
                                {
                                    await official.SendAsync(target,
                                        QOfficialMessage.Media(QOfficialMediaType.Image, image,
                                            $"rss-{feed.Id}.png", caption), reply, cancellationToken)
                                        .ConfigureAwait(false);
                                    captionSent = true;
                                }
                                catch (NotSupportedException)
                                {
                                    image.Position = 0;
                                    await official.SendAsync(target,
                                        QOfficialMessage.Media(QOfficialMediaType.Image, image, $"rss-{feed.Id}.png"),
                                        reply, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            imageSent = true;
                            if (!captionSent && !string.IsNullOrWhiteSpace(caption) && official is not null)
                                await official.SendAsync(target, QOfficialMessage.Text(caption), reply,
                                    cancellationToken).ConfigureAwait(false);
                        }
                        catch (NotSupportedException ex)
                        {
                            BotLog.Warning($"[Rss] QQ 官方适配器暂不支持当前会话的媒体发送，回退 Markdown: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            BotLog.Warning($"[Rss] QQ 官方卡片上传发送失败，回退 Markdown: {ex.GetType().Name}: {ex.Message}");
                        }
                        if (imageSent)
                        {
                            BotLog.Info($"[Rss] QQ 卡片已上传发送 feed={feed.Id} bytes={png.Length} caption={captionSent} target={subscriber.Format()}");
                            return true;
                        }
                    }
                    else
                    {
                        BotLog.Warning($"[Rss] QQ 卡片渲染无结果 feed={feed.Id}，回退 Markdown。");
                    }
                }
            }

            if (official is not null && config.EnableMarkdown)
            {
                var target = new QOfficialMessageTarget(
                    subscriber.Scope == SubscriberScope.Group ? QOfficialMessageScene.Group : QOfficialMessageScene.Direct,
                    subscriber.TargetId);
                var markdown = new QCustomMarkdown(RssMarkdownFormatter.Render(feed, item, config, includeImage));
                var reply = string.IsNullOrWhiteSpace(replyToMessageId)
                    ? null : new QOfficialMessageReply { MessageId = replyToMessageId };
                var keyboard = RssMarkdownFormatter.CreateKeyboard(feed, item, config);
                if (keyboard is not null && official.CanSendMarkdown(target, markdown, keyboard))
                {
                    try
                    {
                        await official.SendAsync(target, QOfficialMessage.Markdown(markdown, keyboard), reply,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        BotLog.Warning($"[Rss] QQ 按钮发送失败，重试无按钮 Markdown: {ex.Message}");
                    }
                }
                try
                {
                    await official.SendAsync(target, QOfficialMessage.Markdown(markdown), reply,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex)
                {
                    BotLog.Warning($"[Rss] QQ Markdown 发送失败，回退普通文本: {ex.Message}");
                }
                if (reply is null)
                    throw new InvalidOperationException("QQPlatform Markdown failed and no recent message is available for text fallback.");
                await _context.Message.SendMessageAsync(channel,
                    [new QuoteSegment(reply.MessageId!), new TextSegment(FormatPreview(feed, item))]).ConfigureAwait(false);
                return true;
            }

            if (string.Equals(subscriber.Platform, "qq-official", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(replyToMessageId))
                    throw new InvalidOperationException("QQPlatform text requires a recent incoming message ID.");
                await _context.Message.SendMessageAsync(channel,
                    [new QuoteSegment(replyToMessageId), new TextSegment(FormatPreview(feed, item))]).ConfigureAwait(false);
                return true;
            }

            var segments = await BuildItemSegmentsAsync(feed, item, includeImage, cancellationToken)
                .ConfigureAwait(false);
            if (replyToMessageId is not null)
                segments = [new QuoteSegment(replyToMessageId), ..segments];
            await _context.Message.SendMessageAsync(channel, segments).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 推送到 {subscriber.Format()} feed={feed.Id} 失败: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private string? RecentMessageId(SubscriberKey key)
    {
        lock (_recent)
        {
            if (_recent.TryGetValue(key, out var entry))
            {
                var lifetime = key.Scope == SubscriberScope.Group ? TimeSpan.FromMinutes(4) : TimeSpan.FromMinutes(55);
                if (DateTimeOffset.UtcNow - entry.SeenAt < lifetime) return entry.MessageId;
            }
        }
        return null;
    }

    /// <summary>Builds a plain message for adapters without the QQPlatform Markdown extension.</summary>
    public async Task<MessageSegment[]> BuildItemSegmentsAsync(
        FeedSource feed, FeedItem item, bool includeImage, CancellationToken cancellationToken)
    {
        var headerName = !string.IsNullOrWhiteSpace(feed.DisplayName) ? feed.DisplayName! : feed.Id;
        var titleLine = string.IsNullOrWhiteSpace(item.Title) ? "(无标题)" : item.Title;

        var tail = new System.Text.StringBuilder();
        tail.Append(Separator).AppendLine().Append('[').Append(headerName).Append(']');
        if (!string.IsNullOrWhiteSpace(item.Link)) tail.AppendLine().Append(item.Link);
        if (item.Published.HasValue)
            tail.AppendLine().Append("发布时间: ").Append(FormatPublishedTime(item.Published.Value));

        if (_configAccessor().EnableRenderedCards && _cardRenderer is not null)
        {
            var png = await _cardRenderer.TryRenderAsync(feed, item, includeImage, cancellationToken)
                .ConfigureAwait(false);
            if (png is { Length: > 0 })
                return [new ImageSegment("base64://" + Convert.ToBase64String(png)),
                    new TextSegment("\n" + tail)];
        }

        ImageSegment? imageSegment = null;
        if (includeImage && !string.IsNullOrWhiteSpace(item.FirstImageUrl))
            imageSegment = await _imageEmbedder.TryBuildAsync(item.FirstImageUrl!, cancellationToken)
                .ConfigureAwait(false);

        if (imageSegment is null)
            return [new TextSegment(titleLine + "\n" + tail)];
        return [new TextSegment(titleLine + "\n"), imageSegment, new TextSegment("\n" + tail)];
    }

    private static string FormatPublishedTime(DateTimeOffset published)
    {
        var local = published.ToLocalTime();
        var delta = DateTimeOffset.Now - local;
        if (delta.TotalSeconds < 0) return local.ToString("yyyy-MM-dd HH:mm");
        if (delta.TotalMinutes < 5) return "[刚刚]";
        if (delta.TotalMinutes < 60) return $"[{(int)delta.TotalMinutes} 分钟前]";
        if (delta.TotalHours < 24) return $"[{(int)delta.TotalHours} 小时前]";
        return local.ToString("yyyy-MM-dd HH:mm");
    }

    public string FormatPreview(FeedSource feed, FeedItem item)
    {
        var headerName = !string.IsNullOrWhiteSpace(feed.DisplayName) ? feed.DisplayName! : feed.Id;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(string.IsNullOrWhiteSpace(item.Title) ? "(无标题)" : item.Title);
        if (!string.IsNullOrWhiteSpace(item.FirstImageUrl))
            builder.Append("封面: ").AppendLine(item.FirstImageUrl);
        builder.AppendLine(Separator).Append('[').Append(headerName).Append(']');
        if (!string.IsNullOrWhiteSpace(item.Link)) builder.AppendLine().Append(item.Link);
        if (item.Published.HasValue)
            builder.AppendLine().Append("发布时间: ").Append(FormatPublishedTime(item.Published.Value));
        return builder.ToString().TrimEnd();
    }
}
