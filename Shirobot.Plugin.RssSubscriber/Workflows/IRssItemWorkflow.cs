using Shirobot.Plugin.RssSubscriber.Feeds;
using Shirobot.Plugin.RssSubscriber.Subscriptions;

namespace Shirobot.Plugin.RssSubscriber.Workflows;

/// <summary>Coordinates an on-demand feed fetch, item selection, card rendering, and delivery.</summary>
public interface IRssItemWorkflow
{
    Task<RssItemWorkflowResult> FetchAndDeliverAsync(
        string feedId,
        string trigger,
        SubscriberKey subscriber,
        Func<IReadOnlyList<FeedItem>, IReadOnlyList<FeedItem>> selectItems,
        string? replyToMessageId,
        bool includeImage,
        CancellationToken cancellationToken);
}

public sealed record RssItemWorkflowResult(
    bool FeedFound,
    bool FetchSucceeded,
    int FetchedItems,
    int SelectedItems,
    int DeliveredItems);
