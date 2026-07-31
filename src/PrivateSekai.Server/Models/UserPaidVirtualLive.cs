using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPaidVirtualLive
{
    [Key("paidVirtualLiveIds")] public int[]? paidVirtualLiveIds;
}
