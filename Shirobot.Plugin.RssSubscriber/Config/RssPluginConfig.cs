using ShiroBot.SDK.Config;

namespace Shirobot.Plugin.RssSubscriber.Config;

[ConfigModel]
public sealed class RssPluginConfig
{
    [ConfigField("启用订阅轮询与推送；关闭后暂停自动检查。", Label = "启用 RSS")]
    public bool Enabled { get; set; } = true;
    [ConfigField("新增订阅未指定间隔时使用的检查周期，不能低于最小轮询间隔。", Label = "默认轮询间隔（秒）")]
    public int DefaultIntervalSeconds { get; set; } = 5;
    [ConfigField("订阅允许设置的最短检查周期，避免过于频繁请求订阅源。", Label = "最小轮询间隔（秒）")]
    public int MinIntervalSeconds { get; set; } = 2;
    [ConfigField("获取 RSS / Atom 订阅源的 HTTP 请求超时时间。", Label = "请求超时（秒）")]
    public int RequestTimeoutSeconds { get; set; } = 30;
    [ConfigField("一次轮询最多推送的新增文章数量。", Label = "每轮推送条数上限")]
    public int MaxItemsPerPush { get; set; } = 3;
    [ConfigField("清除 HTML 后保留的文章摘要最大字符数。", Label = "摘要字符上限")]
    public int MaxDescriptionLength { get; set; } = 200;
    [ConfigField("使用 #rss latest 查询时允许返回的最大文章数量。", Label = "最新文章查询上限")]
    public int LatestMaxN { get; set; } = 5;
    [ConfigField("默认在消息中包含文章的首张图片；订阅可单独覆盖此设置。", Label = "包含文章图片")]
    public bool IncludeImage { get; set; } = false;
    [ConfigField("优先渲染文章卡片；需要宿主启用 Avalonia 渲染，无法渲染时回退文本。", Label = "启用渲染卡片")]
    public bool EnableRenderedCards { get; set; } = true;
    [ConfigField("允许请求内网或本机订阅源，默认只允许公网地址。", Label = "允许内网订阅源")]
    public bool AllowPrivateUrls { get; set; } = false;
    [ConfigField("获取订阅源时发送的 HTTP User-Agent 请求头。", Label = "请求 User-Agent")]
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    [ConfigField("每个订阅源保留的已见文章 ID 数量，用于避免重复推送。", Label = "已读文章缓存数量")]
    public int LastSeenCapacity { get; set; } = 100;
    [ConfigField("订阅源连续获取失败时退避等待的最长时间。", Label = "失败重试最大间隔（秒）")]
    public int BackoffMaxSeconds { get; set; } = 3600;
    [ConfigField("QQ 官方场景在未发送渲染卡片时尝试 Markdown；不可用时回退普通消息。", Label = "启用 Markdown")]
    public bool EnableMarkdown { get; set; } = true;
    [ConfigField("在支持的 QQ 官方消息中附加阅读原文和查看最新按钮。", Label = "启用操作按钮")]
    public bool EnableActionButtons { get; set; } = true;
    [ConfigField("支持 {title}、{description}、{image}、{feed}、{feed_id}、{published}、{link}、{link_button}；留空使用默认模板。", Label = "Markdown 模板", Type = "text")]
    public string MarkdownTemplate { get; set; } =
        "## {title}\n\n{description}\n\n{image}\n\n> 来源：{feed} · {published}\n\n{link_button}";
}
