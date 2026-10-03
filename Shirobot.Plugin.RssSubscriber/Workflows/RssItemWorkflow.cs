using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Scheduler;
using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Workflows;

/// <summary>Shared command workflow for pulling items and delivering them through the common renderer/sender.</summary>
public sealed class RssItemWorkflow(
    RssPollScheduler scheduler,
    FeedRegistry feeds,
    RssDispatcher dispatcher) : IRssItemWorkflow
{
    public async Task<RssItemWorkflowResult> FetchAndDeliverAsync(
        string feedId,
        string trigger,
        SubscriberKey subscriber,
        Func<IReadOnlyList<FeedItem>, IReadOnlyList<FeedItem>> selectItems,
        string? replyToMessageId,
        bool includeImage,
        CancellationToken cancellationToken)
    {
        if (!feeds.TryGet(feedId, out _))
            return new RssItemWorkflowResult(false, false, 0, 0, 0);

        var fetched = await scheduler.FetchOnDemandAsync(feedId, cancellationToken).ConfigureAwait(false);
        if (fetched is null)
            return new RssItemWorkflowResult(true, false, 0, 0, 0);

        if (!feeds.TryGet(feedId, out var feed))
            return new RssItemWorkflowResult(false, true, fetched.Items.Count, 0, 0);

        var selected = selectItems(fetched.Items);
        var delivered = 0;
        foreach (var item in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await dispatcher.SendItemAsync(subscriber, feed, item, replyToMessageId,
                    includeImage, cancellationToken).ConfigureAwait(false))
                delivered++;
        }

        BotLog.Info($"[Rss] 手动流程完成 trigger={trigger} feed={feedId} fetched={fetched.Items.Count} " +
                    $"selected={selected.Count} delivered={delivered} target={subscriber.Format()}");
        return new RssItemWorkflowResult(true, true, fetched.Items.Count, selected.Count, delivered);
    }
}
