using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserArchiveVirtualLiveStatus
{
    [Key("virtualLiveId")] public int virtualLiveId;
    [Key("cheerPoint")] public int cheerPoint;
    [Key("isFinished")] public bool isFinished;
}
