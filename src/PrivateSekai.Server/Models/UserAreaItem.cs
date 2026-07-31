using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAreaItem
{
    [Key("areaItemId")] public int areaItemId;
    [Key("level")] public int level;
}
