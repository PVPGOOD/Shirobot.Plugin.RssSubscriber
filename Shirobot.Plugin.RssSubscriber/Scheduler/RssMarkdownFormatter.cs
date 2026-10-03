using System.Text.RegularExpressions;
using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using ShiroBot.Model.QQ;

namespace Shirobot.Plugin.RssSubscriber.Scheduler;

internal static partial class RssMarkdownFormatter
{
    public static string Render(FeedSource feed, FeedItem item, RssPluginConfig config, bool includeImage)
    {
        var link = SafeUrl(item.Link);
        var image = includeImage ? SafeUrl(item.FirstImageUrl) : null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = Escape(string.IsNullOrWhiteSpace(item.Title) ? "(无标题)" : item.Title),
            ["feed"] = Escape(string.IsNullOrWhiteSpace(feed.DisplayName) ? feed.Id : feed.DisplayName),
            ["feed_id"] = Escape(feed.Id),
            ["description"] = Escape(item.Description),
            ["published"] = item.Published?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未知",
            ["link"] = link ?? string.Empty,
            ["link_button"] = link is null ? string.Empty : $"[阅读原文]({link})",
            ["image"] = image is null ? string.Empty : $"![封面]({image})"
        };
        var template = string.IsNullOrWhiteSpace(config.MarkdownTemplate)
            ? new RssPluginConfig().MarkdownTemplate : config.MarkdownTemplate;
        return Placeholder().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value).Trim();
    }

    public static QInlineKeyboard? CreateKeyboard(FeedSource feed, FeedItem item, RssPluginConfig config)
    {
        if (!config.EnableActionButtons) return null;
        var buttons = new List<QKeyboardButton>();
        if (SafeUrl(item.Link) is { } link)
            buttons.Add(CreateButton("rss-open", "阅读原文", QKeyboardActionType.Jump, link));
        buttons.Add(CreateButton("rss-latest", "查看最新", QKeyboardActionType.Command,
            $"#rss latest {feed.Id}", QKeyboardButtonStyle.Gray));
        return new QInlineKeyboard([new QKeyboardRow(buttons)]);
    }

    private static QKeyboardButton CreateButton(string id, string label, QKeyboardActionType type,
        string data, QKeyboardButtonStyle style = QKeyboardButtonStyle.Blue) => new()
    {
        Id = id,
        RenderData = new QKeyboardRenderData(label, label, style),
        Action = new QKeyboardAction
        {
            Type = type,
            Permission = new QKeyboardPermission { Type = QKeyboardPermissionType.Everyone },
            Data = data,
            UnsupportTips = "请更新 QQ 客户端",
            Enter = type == QKeyboardActionType.Command ? true : null
        }
    };

    private static string? SafeUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal)
            : null;

    private static string Escape(string? value) =>
        Regex.Replace(value ?? string.Empty, @"([\\`*_{}\[\]()#+.!|>~-])", @"\$1");

    [GeneratedRegex(@"\{([a-z_]+)\}")]
    private static partial Regex Placeholder();
}
