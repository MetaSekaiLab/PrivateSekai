extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Home;

public sealed class FriendService(UserSession user, FriendMasterQueries master)
{
    public int Remove(UserState peer, string type)
    {
        var peerId = peer.Data.userRegistration!.userId;
        var own = (user.Data.userFriends ?? []).SingleOrDefault(f => f.opponentUserId == peerId);
        var other = (peer.Data.userFriends ?? []).SingleOrDefault(f => f.opponentUserId == user.UserId);
        if (own == null && other == null && (type is "cancel_friend_request" or "reject_friend_request" or "release_friend"))
            return 200;
        var supported = type switch
        {
            "cancel_friend_request" => own?.friendStatus == "sent_request" && other?.friendStatus == "pending_request",
            "reject_friend_request" => own?.friendStatus == "pending_request" && other?.friendStatus == "sent_request",
            "release_friend" => own?.friendStatus == "friend" && other?.friendStatus == "friend",
            _ => false
        };
        if (!supported) throw new NotSupportedException("尚未核验该好友关系的删除响应。");
        user.Data.userFriends = user.Data.userFriends!.Where(f => f.opponentUserId != peerId).ToArray();
        peer.Data.userFriends = peer.Data.userFriends!.Where(f => f.opponentUserId != user.UserId).ToArray();
        user.MarkChanged(nameof(SuiteUser.userFriends));
        return 200;
    }

    public (int Status, string ErrorCode) Request(UserState peer, PostUserFriendRequest request)
    {
        var peerId = peer.Data.userRegistration!.userId;
        if (request.message == null || request.message.Length > master.Config("friend_request_message_length_limit") ||
            request.sentLocation == null || !Enum.IsDefined(typeof(FriendRequestSentLocation), request.sentLocation) ||
            master.HasNgWord(request.message)) return (400, "");
        var own = (user.Data.userFriends ?? []).SingleOrDefault(f => f.opponentUserId == peerId);
        var other = (peer.Data.userFriends ?? []).SingleOrDefault(f => f.opponentUserId == user.UserId);
        if (own?.friendStatus == "friend" && other?.friendStatus == "friend") return (409, "");
        if (own?.friendStatus == "pending_request" && other?.friendStatus == "sent_request") return (Approve(peer), "");
        var resend = own?.friendStatus == "sent_request" && other?.friendStatus == "pending_request";
        if (!resend && (own != null || other != null))
            throw new NotSupportedException("尚未核验该好友关系的申请转换。");
        if (peer.Data.userConfig?.friendRequestScope == "reject") return (409, "opponent_friend_request_scope_reject");
        if (peer.Data.userConfig?.friendRequestScope == "id_search" && request.sentLocation != "id_search")
            return (409, "opponent_friend_request_scope_id_search");
        if (peer.Data.userConfig?.friendRequestScope is not ("all" or "id_search"))
            throw new NotSupportedException("尚未核验限制申请范围的关系写入。");
        if ((user.Data.userFriends ?? []).Count(f => f.friendStatus == "friend") >= master.Config("friend_count_limit") ||
            (peer.Data.userFriends ?? []).Count(f => f.friendStatus == "friend") >= master.Config("friend_count_limit"))
            throw new NotSupportedException("尚未核验好友数量上限响应。");
        var expires = checked(user.Now + master.Config("friend_request_expire_hour") * 3600000L);
        if (resend)
        {
            own!.requestExpiredAt = other!.requestExpiredAt = expires;
            own.message = other.message = request.message.Length == 0 ? null : request.message;
            user.MarkChanged(nameof(SuiteUser.userFriends));
            return (200, "");
        }
        user.Data.userFriends = [.. user.Data.userFriends ?? [], new UserFriend
        {
            opponentUserId = peerId, friendStatus = "sent_request", requestExpiredAt = expires,
            message = request.message.Length == 0 ? null : request.message
        }];
        peer.Data.userFriends = [.. peer.Data.userFriends ?? [], new UserFriend
        {
            opponentUserId = user.UserId, friendStatus = "pending_request", requestExpiredAt = expires,
            message = request.message.Length == 0 ? null : request.message
        }];
        user.MarkChanged(nameof(SuiteUser.userFriends));
        return (200, "");
    }

    public int Approve(UserState peer)
    {
        var own = (user.Data.userFriends ?? []).SingleOrDefault(f => f.opponentUserId == peer.Data.userRegistration!.userId);
        var other = (peer.Data.userFriends ?? []).SingleOrDefault(f => f.opponentUserId == user.UserId);
        if (own?.friendStatus == "friend" && other?.friendStatus == "friend") return 409;
        if (own?.friendStatus != "pending_request" || other?.friendStatus != "sent_request")
            throw new NotSupportedException("尚未核验该好友关系的接受响应。");
        if (own.requestExpiredAt <= user.Now || other.requestExpiredAt <= user.Now)
            throw new NotSupportedException("尚未核验过期好友申请的接受响应。");
        if ((user.Data.userFriends ?? []).Count(f => f.friendStatus == "friend") >= master.Config("friend_count_limit") ||
            (peer.Data.userFriends ?? []).Count(f => f.friendStatus == "friend") >= master.Config("friend_count_limit"))
            throw new NotSupportedException("尚未核验好友数量上限响应。");
        own.friendStatus = other.friendStatus = "friend";
        own.approvedAt = other.approvedAt = user.Now;
        user.MarkChanged(nameof(SuiteUser.userFriends));
        return 200;
    }
}
