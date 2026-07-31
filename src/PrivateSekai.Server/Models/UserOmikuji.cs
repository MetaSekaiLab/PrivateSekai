using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserOmikuji
{
    [Key("omikujiGroupId")] public int omikujiGroupId;
    [Key("omikujiId")] public int omikujiId;
    [Key("drawCount")] public int drawCount;
}
