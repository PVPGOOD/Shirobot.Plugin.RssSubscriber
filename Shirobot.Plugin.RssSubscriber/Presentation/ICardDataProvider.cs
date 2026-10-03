using Shirobot.Plugin.RssSubscriber.Feeds;

namespace Shirobot.Plugin.RssSubscriber.Presentation;

public interface ICardDataProvider
{
    string SourceId { get; }
    string VariantId { get; }
    Task<CardDataSupplement?> TryLoadAsync(FeedItem item, CancellationToken cancellationToken);
}

public sealed record CardDataSupplement(
    string? AuthorName = null,
    string? AvatarUrl = null,
    string? CoverUrl = null,
    string? DurationText = null,
    string? ViewCountText = null,
    string? DanmakuCountText = null,
    string? LikeCountText = null,
    string? CoinCountText = null,
    string? FavoriteCountText = null,
    string? ShareCountText = null,
    string? CategoryText = null);
