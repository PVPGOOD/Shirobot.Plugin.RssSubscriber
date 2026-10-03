using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Presentation;
using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Scheduler;
using Shirobot.Plugin.RssSubscriber.Sources;
using ShiroBot.SDK.Models;
using Xunit;

namespace Shirobot.Plugin.RssSubscriber.Tests;

public sealed class RssCardArchitectureTests
{
    private static readonly SourceRegistry Sources = SourceRegistry.CreateDefault();
    private static readonly CardRecipeRegistry Recipes = CardRecipeRegistry.CreateDefault();

    [Theory]
    [InlineData("https://github.com/owner/repo", "github", "https://github.com/owner/repo/commits.atom")]
    [InlineData("https://www.bilibili.com/read/cv123", "bilibili", "https://www.bilibili.com/read/cv123")]
    [InlineData("https://x.com/user/status/123", "x", "https://x.com/user/status/123")]
    [InlineData("https://blog.example.org/feed.xml", "general", "https://blog.example.org/feed.xml")]
    [InlineData("https://github.com.evil.example/owner/repo", "general", "https://github.com.evil.example/owner/repo")]
    public void SourceInputUsesExactDomainBoundary(string url, string sourceId, string feedUrl)
    {
        var selection = Sources.ResolveInput(url);
        Assert.Equal(sourceId, selection.Profile.Id);
        Assert.Equal(feedUrl, selection.Input.FeedUrl);
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV123", "video")]
    [InlineData("https://www.bilibili.com/opus/123", "dynamic")]
    [InlineData("https://www.bilibili.com/read/cv123", "article")]
    public void BilibiliUsesArticleLinkToChooseCard(string link, string variant)
    {
        var feed = new FeedSource { Id = "bili", Url = "https://rsshub.example/bilibili/user/dynamic/1" };
        var item = Item(link);
        var source = Sources.ResolveItem(feed, item);
        Assert.Equal("bilibili", source.Id);
        Assert.Equal(variant, Recipes.Select(source.Id, feed, item).VariantId);
    }

    [Fact]
    public void SavedSourceAndUnknownVariantsFallBackCleanly()
    {
        var feed = new FeedSource { Id = "bili", Url = "https://rsshub.example/feed", SourceId = "bilibili" };
        Assert.Equal("bilibili", Sources.ResolveItem(feed, Item("https://example.org/post")).Id);
        Assert.Equal("article", Recipes.Select("unknown", feed, Item("https://example.org/post")).VariantId);
    }

    [Fact]
    public void SourceIdSurvivesFeedRegistrySnapshot()
    {
        var registry = new FeedRegistry();
        Assert.True(registry.TryAddInitialized("video", "https://example.org/feed", "Videos", null,
            [], 100, out _, sourceId: "bilibili"));
        var restored = new FeedRegistry();
        restored.LoadFrom(registry.Snapshot());
        Assert.True(restored.TryGet("video", out var feed));
        Assert.Equal("bilibili", feed.SourceId);
    }

    [Fact]
    public async Task MissingRendererFallsBackToTextWithArticleLink()
    {
        using var http = new HttpClient();
        var dispatcher = new RssDispatcher(null!, () => new RssPluginConfig { EnableRenderedCards = true },
            new ImageEmbedder(http));
        var segments = await dispatcher.BuildItemSegmentsAsync(
            new FeedSource { Id = "blog" }, Item("https://example.org/post"), false, CancellationToken.None);
        var text = Assert.Single(segments.OfType<TextSegment>()).Text;
        Assert.Contains("title", text);
        Assert.Contains("https://example.org/post", text);
    }

    private static FeedItem Item(string link) => new("1", "title", link, "", null, [], null);
}
