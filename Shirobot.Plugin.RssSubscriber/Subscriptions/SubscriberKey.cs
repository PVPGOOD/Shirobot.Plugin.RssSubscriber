namespace Shirobot.Plugin.RssSubscriber.Subscriptions;

public enum SubscriberScope
{
    Group,
    Friend
}

public readonly record struct SubscriberKey(
    SubscriberScope Scope, string Platform, string TargetId, string? InstanceId = null)
{
    // Keep the original key shape for old state files. New subscriptions include
    // the adapter instance so two accounts using the same platform stay isolated.
    public string StorageKey => InstanceId is null
        ? $"{Platform}|{TargetId}"
        : $"instance:{Uri.EscapeDataString(InstanceId)}|{Platform}|{TargetId}";
    public string LegacyStorageKey => $"{Platform}|{TargetId}";

    public string Format()
    {
        var instance = InstanceId is null ? string.Empty : $"@{InstanceId}";
        return Scope switch
        {
            SubscriberScope.Group => $"{Platform}{instance}/group:{TargetId}",
            SubscriberScope.Friend => $"{Platform}{instance}/user:{TargetId}",
            _ => $"unknown:{TargetId}"
        };
    }

    public static SubscriberKey Group(string platform, string groupId, string? instanceId = null) =>
        new(SubscriberScope.Group, platform, groupId, instanceId);
    public static SubscriberKey Friend(string platform, string userId, string? instanceId = null) =>
        new(SubscriberScope.Friend, platform, userId, instanceId);

    public static SubscriberKey FromStorageKey(SubscriberScope scope, string value)
    {
        if (value.StartsWith("instance:", StringComparison.Ordinal))
        {
            var instanceEnd = value.IndexOf('|');
            var platformEnd = instanceEnd < 0 ? -1 : value.IndexOf('|', instanceEnd + 1);
            if (instanceEnd <= "instance:".Length || platformEnd <= instanceEnd + 1 || platformEnd == value.Length - 1)
                throw new FormatException($"订阅键格式无效: {value}");
            var instanceId = Uri.UnescapeDataString(value["instance:".Length..instanceEnd]);
            return new SubscriberKey(scope, value[(instanceEnd + 1)..platformEnd], value[(platformEnd + 1)..], instanceId);
        }

        var separator = value.IndexOf('|');
        if (separator <= 0 || separator == value.Length - 1)
            throw new FormatException($"订阅键格式无效: {value}");
        return new SubscriberKey(scope, value[..separator], value[(separator + 1)..]);
    }
}
