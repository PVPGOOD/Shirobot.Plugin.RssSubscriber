using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Scheduler;

public sealed class RssDispatcher
{
    public const string Separator = "────────";

    private readonly IBotContext _context;
    private readonly RssPluginConfig _config;
    private readonly ImageEmbedder _imageEmbedder;

    public RssDispatcher(IBotContext context, RssPluginConfig config, ImageEmbedder imageEmbedder)
    {
        _context = context;
        _config = config;
        _imageEmbedder = imageEmbedder;
    }

    public async Task PushItemAsync(SubscriberKey subscriber, FeedSource feed, FeedItem item)
    {
        var segments = await BuildItemSegmentsAsync(feed, item, includeImage: _config.IncludeImage, CancellationToken.None);

        try
        {
            if (subscriber.Scope == SubscriberScope.Group)
            {
                await _context.Message.SendGroupMessageAsync(subscriber.TargetId, segments);
            }
            else
            {
                await _context.Message.SendPrivateMessageAsync(subscriber.TargetId, segments);
            }
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 推送到 {subscriber.Format()} feed={feed.Id} 失败: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 构建 item 的消息段，统一供推送和 #rss latest 使用。
    /// 输出顺序: 标题  →  封面图(可选)  →  ────────  →  [DisplayName 或 feedId]  →  正文 URL  →  发布时间。
    /// </summary>
    public async Task<OutgoingSegment[]> BuildItemSegmentsAsync(FeedSource feed, FeedItem item, bool includeImage, CancellationToken cancellationToken)
    {
        var headerName = !string.IsNullOrWhiteSpace(feed.DisplayName) ? feed.DisplayName! : feed.Id;
        var titleLine = string.IsNullOrWhiteSpace(item.Title) ? "(无标题)" : item.Title;

        var tail = new System.Text.StringBuilder();
        tail.Append(Separator);
        tail.AppendLine();
        tail.Append('[').Append(headerName).Append(']');
        if (!string.IsNullOrWhiteSpace(item.Link))
        {
            tail.AppendLine();
            tail.Append(item.Link);
        }
        if (item.Published.HasValue)
        {
            tail.AppendLine();
            tail.Append("发布时间: ").Append(FormatPublishedTime(item.Published.Value));
        }

        ImageOutgoingSegment? imageSegment = null;
        if (includeImage && !string.IsNullOrWhiteSpace(item.FirstImageUrl))
        {
            imageSegment = await _imageEmbedder.TryBuildAsync(item.FirstImageUrl!, cancellationToken);
        }

        if (imageSegment is null)
        {
            return new OutgoingSegment[]
            {
                new TextOutgoingSegment(titleLine + "\n" + tail)
            };
        }

        return new OutgoingSegment[]
        {
            new TextOutgoingSegment(titleLine + "\n"),
            imageSegment,
            new TextOutgoingSegment("\n" + tail)
        };
    }

    /// <summary>
    /// 显示发布时间。距今 1 小时内显示"刚刚"，1-24 小时显示"N 小时前"，否则显示完整时间。
    /// </summary>
    private static string FormatPublishedTime(DateTimeOffset published)
    {
        var local = published.ToLocalTime();
        var delta = DateTimeOffset.Now - local;
        if (delta.TotalSeconds < 0)
        {
            return local.ToString("yyyy-MM-dd HH:mm");
        }

        if (delta.TotalMinutes < 5)
        {
            return "[刚刚]";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"[{(int)delta.TotalMinutes} 分钟前]";
        }

        if (delta.TotalHours < 24)
        {
            return $"[{(int)delta.TotalHours} 小时前]";
        }

        return local.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>
    /// #rss test 等纯文本预览用的格式（不渲染图片，只标记封面 URL）。
    /// </summary>
    public string FormatPreview(FeedSource feed, FeedItem item)
    {
        var headerName = !string.IsNullOrWhiteSpace(feed.DisplayName) ? feed.DisplayName! : feed.Id;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(string.IsNullOrWhiteSpace(item.Title) ? "(无标题)" : item.Title);
        if (!string.IsNullOrWhiteSpace(item.FirstImageUrl))
        {
            builder.Append("封面: ").AppendLine(item.FirstImageUrl);
        }
        builder.AppendLine(Separator);
        builder.Append('[').Append(headerName).Append(']');
        if (!string.IsNullOrWhiteSpace(item.Link))
        {
            builder.AppendLine();
            builder.Append(item.Link);
        }

        if (item.Published.HasValue)
        {
            builder.AppendLine();
            builder.Append("发布时间: ").Append(FormatPublishedTime(item.Published.Value));
        }

        return builder.ToString().TrimEnd();
    }
}
