using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiNormalMissionSheet
{
    [Key("mysekaiNormalMissionSheetId")] public int mysekaiNormalMissionSheetId;
}
