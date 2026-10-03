using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Permissions;

public static class PermissionResolver
{
    public static bool IsBotSuperAdmin(IBotContext context, string senderId)
    {
        return context.IsAdmin(senderId);
    }

    public static bool CanManageGroupSubscription(
        IBotContext context,
        MessageEvent message)
    {
        if (IsBotSuperAdmin(context, message.Sender.Id))
        {
            return true;
        }

        var role = message.Member?.Role ?? MemberRole.Member;
        return role is MemberRole.Owner or MemberRole.Admin;
    }
}
