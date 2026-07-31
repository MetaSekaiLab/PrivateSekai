using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiGateCharacter
{
    [Key("mysekaiGateId")] public int mysekaiGateId;
    [Key("mysekaiGameCharacterUnitGroupId")] public int mysekaiGameCharacterUnitGroupId;
    [Key("isReservation")] public bool isReservation;
    [Key("visitCount")] public int visitCount;
}
