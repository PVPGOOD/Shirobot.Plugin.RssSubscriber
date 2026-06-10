using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Permissions;

public static class PermissionResolver
{
    public static bool IsBotSuperAdmin(IBotContext context, long senderId)
    {
        return context.IsAdmin(senderId);
    }

    public static bool CanManageGroupSubscription(
        IBotContext context,
        GroupIncomingMessage message)
    {
        if (IsBotSuperAdmin(context, message.SenderId))
        {
            return true;
        }

        var role = message.GroupMember?.Role ?? GroupMemberEntityRole.Member;
        return role is GroupMemberEntityRole.Owner or GroupMemberEntityRole.Admin;
    }
}
