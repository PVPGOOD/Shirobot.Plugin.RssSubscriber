using Shirobot.Plugin.RssSubscriber.Subscriptions;
using ShiroBot.SDK.Models;

namespace Shirobot.Plugin.RssSubscriber.Commands;

public sealed class CommandContext
{
    public CommandContext(
        MessageEvent sourceMessage,
        SubscriberKey scope,
        string senderId,
        bool isAdminScope,
        Func<string, IEnumerable<MessageSegment>?, Task> replyAsync,
        Func<bool, string, IEnumerable<MessageSegment>?, Task> replyMentionAsync)
    {
        SourceMessage = sourceMessage;
        Scope = scope;
        SenderId = senderId;
        IsAdminScope = isAdminScope;
        ReplyAsync = replyAsync;
        ReplyMentionAsync = replyMentionAsync;
    }

    public MessageEvent SourceMessage { get; }
    public SubscriberKey Scope { get; }
    public string SenderId { get; }
    public bool IsAdminScope { get; }

    /// <summary>普通文本回复，不带 @。</summary>
    public Func<string, IEnumerable<MessageSegment>?, Task> ReplyAsync { get; }

    /// <summary>群里第一参数 mention=true 时会前置 @ 操作者；私聊一律不 @。</summary>
    public Func<bool, string, IEnumerable<MessageSegment>?, Task> ReplyMentionAsync { get; }

}
