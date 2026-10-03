using Avalonia.Controls;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Presentation.Cards.Bilibili;
using Shirobot.Plugin.RssSubscriber.Presentation.Cards.Generic;
using Shirobot.Plugin.RssSubscriber.Presentation.Cards.Git;
using Shirobot.Plugin.RssSubscriber.Presentation.Cards.WordPress;
using Shirobot.Plugin.RssSubscriber.Presentation.Cards.X;
using ShiroBot.AvaloniaSdk;

namespace Shirobot.Plugin.RssSubscriber.Presentation;

public interface ICardRecipe
{
    string SourceId { get; }
    string VariantId { get; }
    Task<byte[]> RenderAsync(IAvaloniaRenderContext renderer, CardViewModel model, CancellationToken cancellationToken);
}

public sealed class CardRecipe<TControl>(string sourceId, string variantId) : ICardRecipe
    where TControl : Control, new()
{
    public string SourceId { get; } = sourceId;
    public string VariantId { get; } = variantId;

    public Task<byte[]> RenderAsync(IAvaloniaRenderContext renderer, CardViewModel model,
        CancellationToken cancellationToken) =>
        renderer.RenderControlPngAsync<TControl>(model, new ControlRenderOptions(Dpi: 192), cancellationToken);
}

public sealed class CardRecipeRegistry
{
    private readonly Dictionary<string, ICardRecipe> _recipes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ICardVariantResolver> _resolvers = new(StringComparer.OrdinalIgnoreCase);
    private readonly FeedPlatformRegistry _feedPlatforms;

    public CardRecipeRegistry(IEnumerable<ICardRecipe> recipes, IEnumerable<ICardVariantResolver> resolvers,
        FeedPlatformRegistry? feedPlatforms = null)
    {
        _feedPlatforms = feedPlatforms ?? FeedPlatformRegistry.CreateDefault();
        foreach (var recipe in recipes)
            if (!_recipes.TryAdd(Key(recipe.SourceId, recipe.VariantId), recipe))
                throw new ArgumentException($"重复的卡片方案: {recipe.SourceId}/{recipe.VariantId}", nameof(recipes));
        foreach (var resolver in resolvers)
            if (!_resolvers.TryAdd(resolver.SourceId, resolver))
                throw new ArgumentException($"重复的卡片类型解析器: {resolver.SourceId}", nameof(resolvers));
        if (!_recipes.ContainsKey(Key("general", "article")))
            throw new ArgumentException("缺少通用文章卡片。", nameof(recipes));
    }

    public static CardRecipeRegistry CreateDefault(FeedPlatformRegistry? feedPlatforms = null) => new(
        [
            new CardRecipe<Cards.Generic.ArticleCard>("general", "article"),
            new CardRecipe<WordPressArticleCard>("wordpress", "article"),
            new CardRecipe<PostCard>("x", "post"),
            new CardRecipe<Cards.Bilibili.ArticleCard>("bilibili", "article"),
            new CardRecipe<VideoCard>("bilibili", "video"),
            new CardRecipe<DynamicCard>("bilibili", "dynamic"),
            new CardRecipe<GitEventCard>("github", "commit"),
            new CardRecipe<GitEventCard>("github", "pull_request"),
            new CardRecipe<GitEventCard>("github", "issue"),
            new CardRecipe<GitEventCard>("github", "activity"),
            new CardRecipe<GitEventCard>("gitea", "commit"),
            new CardRecipe<GitEventCard>("gitea", "pull_request"),
            new CardRecipe<GitEventCard>("gitea", "issue"),
            new CardRecipe<GitEventCard>("gitea", "activity")
        ],
        [new XCardVariantResolver(), new BilibiliCardVariantResolver(),
            new GitCardVariantResolver("github"), new GitCardVariantResolver("gitea")], feedPlatforms);

    public ICardRecipe Select(string sourceId, FeedSource feed, FeedItem item)
    {
        var recipeSourceId = sourceId.Equals("general", StringComparison.OrdinalIgnoreCase) &&
                             _feedPlatforms.Resolve(feed.Generator)?.Id == "wordpress"
            ? "wordpress"
            : sourceId;
        var variant = _resolvers.TryGetValue(sourceId, out var resolver)
            ? resolver.Resolve(feed, item) : "article";
        if (_recipes.TryGetValue(Key(recipeSourceId, variant), out var recipe)) return recipe;
        if (_recipes.TryGetValue(Key(recipeSourceId, "article"), out recipe)) return recipe;
        if (_recipes.TryGetValue(Key(sourceId, "article"), out recipe)) return recipe;
        return _recipes[Key("general", "article")];
    }

    private static string Key(string sourceId, string variantId) => $"{sourceId}/{variantId}";
}
