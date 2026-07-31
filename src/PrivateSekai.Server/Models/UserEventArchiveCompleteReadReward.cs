using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserEventArchiveCompleteReadReward
{
    [Key("eventStoryId")] public int eventStoryId;
    [Key("isDisplayEventArchiveCompleteReadProgress")] public bool isDisplayEventArchiveCompleteReadProgress;
}
