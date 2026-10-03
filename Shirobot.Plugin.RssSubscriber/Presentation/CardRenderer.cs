using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Presentation.Git;
using Shirobot.Plugin.RssSubscriber.Sources;
using ShiroBot.AvaloniaSdk;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Presentation;

public sealed class CardRenderer(
    IRenderContext? renderer,
    SourceRegistry sources,
    CardRecipeRegistry recipes,
    CardMediaLoader media,
    IReadOnlyList<ICardDataProvider>? dataProviders = null,
    FeedPlatformRegistry? feedPlatforms = null)
{
    private readonly FeedPlatformRegistry _feedPlatforms = feedPlatforms ?? FeedPlatformRegistry.CreateDefault();

    public string ResolveSourceName(FeedSource feed, FeedItem item) =>
        _feedPlatforms.Resolve(feed.Generator)?.DisplayName ??
        (sources.ResolveItem(feed, item).Id == "general" ? "RSS" : sources.ResolveItem(feed, item).DisplayName);

    public bool RendersFeedMetadataInCard(FeedSource feed) =>
        _feedPlatforms.Resolve(feed.Generator)?.Id.Equals("wordpress", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<byte[]?> TryRenderAsync(FeedSource feed, FeedItem item, bool includeCover,
        CancellationToken cancellationToken)
    {
        if (renderer is not IAvaloniaRenderContext avalonia)
        {
            BotLog.Warning($"[Rss] Avalonia 卡片渲染器不可用 feed={feed.Id} renderer={renderer?.GetType().FullName ?? "null"}");
            return null;
        }

        var source = sources.ResolveItem(feed, item);
        var platform = _feedPlatforms.Resolve(feed.Generator);
        var recipe = recipes.Select(source.Id, feed, item);
        var gitInfo = source.Id is "github" or "gitea" ? GitCardInfo.From(item) : null;
        BotLog.Info($"[Rss] 选择卡片 feed={feed.Id} source={source.Id} variant={recipe.VariantId}");
        try
        {
            var supplement = dataProviders?.FirstOrDefault(provider =>
                    provider.SourceId.Equals(source.Id, StringComparison.OrdinalIgnoreCase) &&
                    provider.VariantId.Equals(recipe.VariantId, StringComparison.OrdinalIgnoreCase)) is { } provider
                ? await provider.TryLoadAsync(item, cancellationToken).ConfigureAwait(false)
                : null;
            var avatarUrl = supplement?.AvatarUrl ?? feed.FeedImageUrl;
            if (string.IsNullOrWhiteSpace(avatarUrl) && source.Id == "general")
                avatarUrl = await media.TryResolveSiteAvatarUrlAsync(item.Link, cancellationToken).ConfigureAwait(false);
            using var model = new CardViewModel
            {
                SourceName = platform?.DisplayName ?? (source.Id == "general" ? "RSS" : source.DisplayName),
                SitePlatformName = platform?.DisplayName ?? (source.Id == "general" ? "RSS" : source.DisplayName),
                Title = string.IsNullOrWhiteSpace(item.Title) ? "(无标题)" : item.Title,
                Description = BuildCardDescription(item),
                FeedName = string.IsNullOrWhiteSpace(feed.DisplayName) ? feed.Id : feed.DisplayName,
                AuthorName = string.IsNullOrWhiteSpace(supplement?.AuthorName)
                    ? string.IsNullOrWhiteSpace(feed.DisplayName) ? feed.Id : feed.DisplayName
                    : supplement.AuthorName,
                PublishedText = item.Published?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
                PublishedDateText = item.Published?.ToLocalTime().ToString("MM-dd 发布") ?? string.Empty,
                Link = item.Link,
                TagsText = string.Join("  ", item.Tags.Take(4).Select(tag => "#" + tag)),
                Avatar = await media.TryLoadAsync(avatarUrl, cancellationToken)
                    .ConfigureAwait(false),
                DurationText = supplement?.DurationText ?? string.Empty,
                ViewCountText = supplement?.ViewCountText ?? string.Empty,
                DanmakuCountText = supplement?.DanmakuCountText ?? string.Empty,
                LikeCountText = supplement?.LikeCountText ?? string.Empty,
                CoinCountText = supplement?.CoinCountText ?? string.Empty,
                FavoriteCountText = supplement?.FavoriteCountText ?? string.Empty,
                ShareCountText = supplement?.ShareCountText ?? string.Empty,
                CategoryText = !string.IsNullOrWhiteSpace(supplement?.CategoryText)
                    ? supplement.CategoryText
                    : string.Join(" / ", item.Tags),
                GitEventTypeText = gitInfo?.EventType ?? string.Empty,
                GitStatusText = gitInfo?.Status ?? string.Empty,
                GitRepositoryText = gitInfo?.Repository ?? string.Empty,
                GitReferenceText = gitInfo?.Reference ?? string.Empty,
                Cover = includeCover
                    ? await media.TryLoadAsync(item.FirstImageUrl ?? supplement?.CoverUrl, cancellationToken)
                        .ConfigureAwait(false)
                    : null
            };
            return await recipe.RenderAsync(avalonia, model, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 卡片渲染失败 feed={feed.Id} source={source.Id} variant={recipe.VariantId}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string BuildCardDescription(FeedItem item)
    {
        var description = string.IsNullOrWhiteSpace(item.FullDescription)
            ? item.Description
            : item.FullDescription;
        var title = item.Title?.Trim();
        if (!string.IsNullOrWhiteSpace(title) &&
            description.StartsWith(title, StringComparison.OrdinalIgnoreCase))
            description = description[title.Length..].TrimStart(' ', '\r', '\n', '\t', '·', '-', '—');
        return description;
    }
}
