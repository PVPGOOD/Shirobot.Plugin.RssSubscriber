using Avalonia.Media.Imaging;

namespace Shirobot.Plugin.RssSubscriber.Presentation;

public sealed class CardViewModel : IDisposable
{
    public string SourceName { get; init; } = string.Empty;
    public string SourceLabel => $"来源: {SourceName}";
    public string SitePlatformName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FeedName { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string PublishedText { get; init; } = string.Empty;
    public string PublishedDateText { get; init; } = string.Empty;
    public bool HasPublishedDate => !string.IsNullOrWhiteSpace(PublishedDateText);
    public string PublishedLabel => $"发布时间: {(string.IsNullOrWhiteSpace(PublishedText) ? "未知" : PublishedText)}";
    public string Link { get; init; } = string.Empty;
    public string TagsText { get; init; } = string.Empty;
    public bool HasTags => !string.IsNullOrWhiteSpace(TagsText);
    public string DurationText { get; init; } = string.Empty;
    public bool HasDuration => !string.IsNullOrWhiteSpace(DurationText);
    public string ViewCountText { get; init; } = string.Empty;
    public bool HasViewCount => !string.IsNullOrWhiteSpace(ViewCountText);
    public string DanmakuCountText { get; init; } = string.Empty;
    public bool HasDanmakuCount => !string.IsNullOrWhiteSpace(DanmakuCountText);
    public string LikeCountText { get; init; } = string.Empty;
    public bool HasLikeCount => !string.IsNullOrWhiteSpace(LikeCountText);
    public string CoinCountText { get; init; } = string.Empty;
    public bool HasCoinCount => !string.IsNullOrWhiteSpace(CoinCountText);
    public string FavoriteCountText { get; init; } = string.Empty;
    public bool HasFavoriteCount => !string.IsNullOrWhiteSpace(FavoriteCountText);
    public string ShareCountText { get; init; } = string.Empty;
    public bool HasShareCount => !string.IsNullOrWhiteSpace(ShareCountText);
    public string CategoryText { get; init; } = string.Empty;
    public bool HasCategory => !string.IsNullOrWhiteSpace(CategoryText);
    public bool HasVideoStats => !string.IsNullOrWhiteSpace(ViewCountText) ||
                                 !string.IsNullOrWhiteSpace(LikeCountText) ||
                                 !string.IsNullOrWhiteSpace(CoinCountText) ||
                                 !string.IsNullOrWhiteSpace(FavoriteCountText) ||
                                 !string.IsNullOrWhiteSpace(ShareCountText);
    public string ViewCountLabel => $"{ViewCountText} 播放";
    public string LikeCountLabel => $"{LikeCountText} 点赞";
    public string CoinCountLabel => $"{CoinCountText} 投币";
    public string FavoriteCountLabel => $"{FavoriteCountText} 收藏";
    public string ShareCountLabel => $"{ShareCountText} 转发";
    public string GitEventTypeText { get; init; } = string.Empty;
    public string GitStatusText { get; init; } = string.Empty;
    public bool HasGitStatus => !string.IsNullOrWhiteSpace(GitStatusText);
    public string GitRepositoryText { get; init; } = string.Empty;
    public string GitReferenceText { get; init; } = string.Empty;
    public bool HasGitReference => !string.IsNullOrWhiteSpace(GitReferenceText);
    public Bitmap? Avatar { get; init; }
    public bool HasAvatar => Avatar is not null;
    public Bitmap? Cover { get; init; }
    public bool HasCover => Cover is not null;

    public void Dispose()
    {
        Avatar?.Dispose();
        Cover?.Dispose();
    }
}
