using Shirobot.Plugin.RssSubscriber.Storage;
using Xunit;

namespace Shirobot.Plugin.RssSubscriber.Tests;

public sealed class RssStateStoreTests
{
    [Fact]
    public async Task PollingOnlyWritesRuntimeState()
    {
        var directory = NewDirectory();
        try
        {
            var state = new RssState
            {
                Feeds = new Dictionary<string, PersistentFeed>
                {
                    ["news"] = new()
                    {
                        Url = "https://example.test/rss",
                        DisplayName = "News",
                        IntervalSeconds = 20,
                        LastSeenGuids = ["old"],
                        LastFetchAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                        ConsecutiveFailures = 2,
                        ETag = "\"v1\""
                    }
                },
                GroupSubs = new Dictionary<string, List<string>> { ["qq|group"] = ["news"] },
                DeliveryReceipts = new Dictionary<string, Dictionary<string, List<string>>>
                {
                    ["news"] = new() { ["new"] = ["qq|group"] }
                }
            };
            var store = new RssStateStore(directory);
            store.SaveSync(state);

            var loaded = store.Load();

            Assert.Equal("https://example.test/rss", loaded.Feeds["news"].Url);
            Assert.Equal("News", loaded.Feeds["news"].DisplayName);
            Assert.Equal(["old"], loaded.Feeds["news"].LastSeenGuids);
            Assert.Equal(2, loaded.Feeds["news"].ConsecutiveFailures);
            Assert.Equal(["news"], loaded.GroupSubs["qq|group"]);
            Assert.Equal(["qq|group"], loaded.DeliveryReceipts["news"]["new"]);

            var subscriptionsJson = File.ReadAllText(store.SubscriptionsFilePath);
            var runtimeJson = File.ReadAllText(store.FilePath);
            Assert.Contains("groupSubs", subscriptionsJson);
            Assert.DoesNotContain("lastSeen", subscriptionsJson);
            Assert.DoesNotContain("displayName", subscriptionsJson);
            Assert.Contains("lastSeen", runtimeJson);
            Assert.Contains("displayName", runtimeJson);
            Assert.DoesNotContain("groupSubs", runtimeJson);
            Assert.DoesNotContain("https://example.test/rss", runtimeJson);

            loaded.Feeds["news"].ConsecutiveFailures = 0;
            await store.SaveAsync(loaded);
            Assert.Equal(subscriptionsJson, File.ReadAllText(store.SubscriptionsFilePath));
            Assert.Equal(0, store.Load().Feeds["news"].ConsecutiveFailures);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EmptyDirectoryStartsWithNoSubscriptions()
    {
        var directory = NewDirectory();
        try
        {
            var store = new RssStateStore(directory);
            var state = store.Load();

            Assert.Empty(state.Feeds);
            Assert.Empty(state.GroupSubs);
            Assert.Empty(state.FriendSubs);
            Assert.True(File.Exists(store.SubscriptionsFilePath));
            Assert.Empty(store.Load().Feeds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BrokenRuntimeStateKeepsSubscriptionsForBaselineRecovery()
    {
        var directory = NewDirectory();
        try
        {
            var store = new RssStateStore(directory);
            store.SaveSync(new RssState
            {
                Feeds = new Dictionary<string, PersistentFeed>
                {
                    ["news"] = new() { Url = "https://example.test/rss", LastSeenGuids = ["old"] }
                },
                FriendSubs = new Dictionary<string, List<string>> { ["qq|friend"] = ["news"] }
            });
            File.WriteAllText(store.FilePath, "{");

            var recovered = store.Load();

            Assert.Equal("https://example.test/rss", recovered.Feeds["news"].Url);
            Assert.Equal(["news"], recovered.FriendSubs["qq|friend"]);
            Assert.Empty(recovered.Feeds["news"].LastSeenGuids);
            Assert.Null(recovered.Feeds["news"].LastFetchAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingSubscriptionsFileCreatesEmptySubscriptionsWithoutRevivingOldFeeds()
    {
        var directory = NewDirectory();
        try
        {
            var store = new RssStateStore(directory);
            store.SaveSync(new RssState
            {
                Feeds = new Dictionary<string, PersistentFeed>
                {
                    ["news"] = new() { Url = "https://example.test/rss" }
                },
                DeliveryReceipts = new Dictionary<string, Dictionary<string, List<string>>>
                {
                    ["news"] = new() { ["old"] = ["qq|group"] }
                }
            });
            var runtimeJson = File.ReadAllText(store.FilePath);
            File.Delete(store.SubscriptionsFilePath);

            var loaded = store.Load();

            Assert.True(File.Exists(store.SubscriptionsFilePath));
            Assert.Empty(loaded.Feeds);
            Assert.Empty(loaded.GroupSubs);
            Assert.Empty(loaded.FriendSubs);
            Assert.Empty(loaded.DeliveryReceipts);
            Assert.Equal(runtimeJson, File.ReadAllText(store.FilePath));
            Assert.Empty(store.Load().Feeds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Shirobot-Rss-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
