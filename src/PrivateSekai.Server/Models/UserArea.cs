using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserArea
{
    [Key("areaId")] public int areaId;
    [Key("actionSets")] public UserActionSet[]? actionSets;
    [Key("areaItems")] public UserAreaItem[]? areaItems;
    [Key("userAreaStatus")] public UserAreaStatus? userAreaStatus;
}
