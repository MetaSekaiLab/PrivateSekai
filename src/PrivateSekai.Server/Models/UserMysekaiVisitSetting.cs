using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiVisitSetting
{
    [Key("mysekaiRoomAcceptUserType")] public string? mysekaiRoomAcceptUserType;
}
