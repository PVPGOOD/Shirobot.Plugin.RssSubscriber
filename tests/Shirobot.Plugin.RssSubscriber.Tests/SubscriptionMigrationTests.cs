using Shirobot.Plugin.RssSubscriber.Subscriptions;
using Xunit;

namespace Shirobot.Plugin.RssSubscriber.Tests;

public sealed class SubscriptionMigrationTests
{
    [Fact]
    public void ExplicitBindingMovesOnlySelectedLegacyFeedAndReportsStateChange()
    {
        var registry = new SubscriptionRegistry();
        registry.LoadFrom(new Dictionary<string, List<string>> { ["qq|group"] = ["first", "second"] }, new Dictionary<string, List<string>>());
        var first = SubscriberKey.Group("qq", "group", "first-instance");
        var second = SubscriberKey.Group("qq", "group", "second-instance");
        Assert.True(registry.Add(first, "first"));
        Assert.Equal(new[] { "first" }, registry.List(first));
        Assert.Equal(new[] { "second" }, registry.SnapshotGroups()["qq|group"]);
        Assert.True(registry.Add(second, "second"));
        Assert.Equal(new[] { "second" }, registry.List(second));
        Assert.False(registry.SnapshotGroups().ContainsKey("qq|group"));
        Assert.False(registry.Add(first, "first"));
    }
}
