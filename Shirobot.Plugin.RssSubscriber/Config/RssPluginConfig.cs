namespace Shirobot.Plugin.RssSubscriber.Config;

public sealed class RssPluginConfig
{
    public bool Enabled { get; set; } = true;
    public int DefaultIntervalSeconds { get; set; } = 10;
    public int MinIntervalSeconds { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaxItemsPerPush { get; set; } = 3;
    public int MaxDescriptionLength { get; set; } = 200;
    public int LatestMaxN { get; set; } = 5;
    public bool IncludeImage { get; set; } = false;
    public bool AllowPrivateUrls { get; set; } = false;
    public string UserAgent { get; set; } = "Shirobot-Rss/1.0";
    public int LastSeenCapacity { get; set; } = 100;
    public int BackoffMaxSeconds { get; set; } = 3600;
}
