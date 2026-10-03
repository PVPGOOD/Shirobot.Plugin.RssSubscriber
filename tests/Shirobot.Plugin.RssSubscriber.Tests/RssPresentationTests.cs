using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Commands;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Scheduler;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.Model.QQ;
using ShiroBot.SDK.Models;
using Xunit;

namespace Shirobot.Plugin.RssSubscriber.Tests;

public sealed class RssPresentationTests
{
    [Fact]
    public void QQPlatformRepliesDoNotRenderOpenIdAsMention()
    {
        var message = new MessageEvent
        {
            Platform = "qq-official",
            MessageId = "incoming",
            Channel = Channel.Group("group-openid"),
            Sender = new User("7C776D0A8A0B3C63156F4724DCCC311F"),
            Segments = [new TextSegment("#rss add")]
        };
        var segments = RssCommandHandler.BuildReplySegments(message, true, "[RSS] 已添加并订阅。");
        Assert.Equal("[RSS] 已添加并订阅。", Assert.Single(segments.OfType<TextSegment>()).Text);
        Assert.DoesNotContain(segments, segment => segment is MentionSegment);

        var milky = RssCommandHandler.BuildReplySegments(message with { Platform = "qq" }, true, "完成");
        Assert.Equal(message.Sender.Id, Assert.Single(milky.OfType<MentionSegment>()).UserId);
    }

    [Fact]
    public void OpenIdSubscribersSurviveStateRoundTrip()
    {
        var registry = new SubscriptionRegistry();
        var key = SubscriberKey.Group("qq-official", "FDC67211C475FC5275F32B3E92430889");
        registry.Add(key, "news");
        var saved = registry.SnapshotGroups();
        var restored = new SubscriptionRegistry();
        restored.LoadFrom(saved, new Dictionary<string, List<string>>());
        Assert.Equal(key, Assert.Single(restored.SubscribersOf("news")));
        Assert.Equal([key.StorageKey], saved.Keys);
        Assert.Throws<FormatException>(() => SubscriberKey.FromStorageKey(SubscriberScope.Group, "12345"));
    }

    [Fact]
    public void MarkdownTemplateRendersFeedFieldsAndSafeButtons()
    {
        var config = new RssPluginConfig { IncludeImage = true };
        var feed = new FeedSource { Id = "news", DisplayName = "技术*周刊" };
        var item = new FeedItem("1", "A [title]", "https://example.org/post", "正文", null, [], "https://example.org/a.png");
        var markdown = RssMarkdownFormatter.Render(feed, item, config, includeImage: true);
        Assert.Contains("A \\[title\\]", markdown);
        Assert.Contains("技术\\*周刊", markdown);
        Assert.Contains("![封面](https://example.org/a.png)", markdown);
        Assert.Contains("[阅读原文](https://example.org/post)", markdown);
        var buttons = Assert.Single(RssMarkdownFormatter.CreateKeyboard(feed, item, config)!.Rows).Buttons;
        Assert.Equal(QKeyboardActionType.Jump, buttons[0].Action.Type);
        Assert.Equal(QKeyboardActionType.Command, buttons[1].Action.Type);
        Assert.Equal("#rss latest news", buttons[1].Action.Data);
    }

    [Fact]
    public void UnsafeArticleUrlDoesNotBecomeActionButton()
    {
        var config = new RssPluginConfig();
        var feed = new FeedSource { Id = "news" };
        var item = new FeedItem("1", "title", "javascript:alert(1)", "", null, [], null);
        var buttons = Assert.Single(RssMarkdownFormatter.CreateKeyboard(feed, item, config)!.Rows).Buttons;
        Assert.Single(buttons);
        Assert.Equal(QKeyboardActionType.Command, buttons[0].Action.Type);
        Assert.DoesNotContain("javascript:", RssMarkdownFormatter.Render(feed, item, config, includeImage: false));
    }
}
