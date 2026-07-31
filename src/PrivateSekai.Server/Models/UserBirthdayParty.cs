using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBirthdayParty
{
    [Key("userId")] public long userId;
    [Key("birthdayPartyId")] public int birthdayPartyId;
    [Key("deliveryTotalPoint")] public int deliveryTotalPoint;
    [Key("droppedMysekaiMaterialCount")] public int droppedMysekaiMaterialCount;
    [Key("obtainedMysekaiMaterialCount")] public int obtainedMysekaiMaterialCount;
}
