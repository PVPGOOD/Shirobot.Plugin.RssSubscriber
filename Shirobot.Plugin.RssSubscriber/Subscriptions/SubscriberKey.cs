namespace Shirobot.Plugin.RssSubscriber.Subscriptions;

public enum SubscriberScope
{
    Group,
    Friend
}

public readonly record struct SubscriberKey(SubscriberScope Scope, long TargetId)
{
    public string Format() => Scope switch
    {
        SubscriberScope.Group => $"group:{TargetId}",
        SubscriberScope.Friend => $"user:{TargetId}",
        _ => $"unknown:{TargetId}"
    };

    public static SubscriberKey Group(long groupId) => new(SubscriberScope.Group, groupId);
    public static SubscriberKey Friend(long userId) => new(SubscriberScope.Friend, userId);
}
