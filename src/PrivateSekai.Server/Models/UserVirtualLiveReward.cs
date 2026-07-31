using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserVirtualLiveReward
{
    [Key("virtualLiveId")] public int virtualLiveId;
    [Key("joinRewardReceivedFlg")] public bool joinRewardReceivedFlg;
    [Key("cheerPointRewardReceivedFlg")] public bool cheerPointRewardReceivedFlg;
    [Key("memberCountRewardReceivedFlg")] public bool memberCountRewardReceivedFlg;
}
