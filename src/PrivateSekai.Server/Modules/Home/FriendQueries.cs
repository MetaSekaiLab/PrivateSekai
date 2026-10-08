extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Home;

public sealed class FriendQueries(UserOperation operations)
{
    public SuiteUser Project(SuiteUser result)
    {
        if (result.userFriends == null) return result;
        result = DumpSerializer.Deserialize<SuiteUser>(DumpSerializer.Serialize(result));
        foreach (var friend in result.userFriends ?? [])
        {
            var opponent = operations.Read(friend.opponentUserId);
            if (opponent == null) continue;
            var data = opponent.Data;
            var profile = data.userProfile ?? throw new InvalidOperationException("缺少好友个人资料。");
            var cardId = profile.profileImageType switch
            {
                "leader" => (data.userDecks ?? []).Single(d => d.deckId == data.userGamedata.deck).leader,
                _ => throw new NotSupportedException("尚未核验该好友头像类型。")
            };
            var card = (data.userCards ?? []).Single(c => c.cardId == cardId);
            if ((data.userProfileHonors ?? []).Length != 0)
                throw new NotSupportedException("尚未核验好友称号资料映射。");
            if (data.userMysekaiVisitSetting != null || (data.userPlayerFrames ?? []).Length != 0)
                throw new NotSupportedException("尚未核验好友 Mysekai 设置或玩家边框映射。");
            friend.opponentUserFriendProfile = new UserFriendProfile
            {
                name = data.userGamedata.name,
                userProfile = new UserProfile { userId = friend.opponentUserId, profileImageType = profile.profileImageType, profileImageId = profile.profileImageId },
                userCard = DumpSerializer.Deserialize<UserCard>(DumpSerializer.Serialize(card)),
                userProfileHonors = [], userHonorMissions = [], userPlayerFrames = [],
                isMysekaiOwnerAcceptVisitForFriend = false
            };
            if (data.userConfig?.isDisplayLoginStatus == false)
                friend.userLoginStatus = new UserLoginStatus { loginStatus = "offline" };
            else if (opponent.Private.LoginStatus is { } status)
                friend.userLoginStatus = status;
        }
        return result;
    }
}
