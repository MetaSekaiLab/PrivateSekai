using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPaidVirtualLiveStatus
{
    [Key("paidVirtualLiveId")] public int paidVirtualLiveId;
    [Key("cheerPoint")] public int cheerPoint;
    [Key("isFinished")] public bool isFinished;
}
