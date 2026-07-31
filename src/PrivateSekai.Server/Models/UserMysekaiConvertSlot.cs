using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiConvertSlot
{
    [Key("slotId")] public int slotId;
    [Key("mysekaiConvertItemId")] public int mysekaiConvertItemId;
    [Key("choiceId")] public int choiceId;
    [Key("convertStartAt")] public long convertStartAt;
    [Key("onceConvertMinutes")] public int onceConvertMinutes;
    [Key("convertCount")] public int convertCount;
    [Key("receivedProgress")] public int receivedProgress;
    [Key("mysekaiConvertSlotStatus")] public string? mysekaiConvertSlotStatus;
    [Key("userMysekaiConvertObtainResources")] public List<UserMysekaiConvertObtainResource>? userMysekaiConvertObtainResources;
}
