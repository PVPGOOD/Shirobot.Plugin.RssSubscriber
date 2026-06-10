using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Commands;

public sealed class CommandContext
{
    public CommandContext(
        IBotContext bot,
        SubscriberKey scope,
        long senderId,
        bool isAdminScope,
        bool isGroup,
        long? groupId,
        Func<string, IEnumerable<OutgoingSegment>?, Task> replyAsync,
        Func<bool, string, IEnumerable<OutgoingSegment>?, Task> replyMentionAsync,
        Func<OutgoingSegment[], Task> sendSegmentsAsync)
    {
        Bot = bot;
        Scope = scope;
        SenderId = senderId;
        IsAdminScope = isAdminScope;
        IsGroup = isGroup;
        GroupId = groupId;
        ReplyAsync = replyAsync;
        ReplyMentionAsync = replyMentionAsync;
        SendSegmentsAsync = sendSegmentsAsync;
    }

    public IBotContext Bot { get; }
    public SubscriberKey Scope { get; }
    public long SenderId { get; }
    public bool IsAdminScope { get; }
    public bool IsGroup { get; }
    public long? GroupId { get; }

    /// <summary>普通文本回复，不带 @。</summary>
    public Func<string, IEnumerable<OutgoingSegment>?, Task> ReplyAsync { get; }

    /// <summary>群里第一参数 mention=true 时会前置 @ 操作者；私聊一律不 @。</summary>
    public Func<bool, string, IEnumerable<OutgoingSegment>?, Task> ReplyMentionAsync { get; }

    /// <summary>直接按已构建好的 segments 发送，常用于推送/latest 复用 dispatcher 的拼装。</summary>
    public Func<OutgoingSegment[], Task> SendSegmentsAsync { get; }
}
