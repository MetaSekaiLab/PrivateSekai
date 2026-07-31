using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiCanvas
{
    [Key("mysekaiFixtureId")] public int mysekaiFixtureId;
    [Key("cardId")] public int cardId;
    [Key("isSpecialTraining")] public bool isSpecialTraining;
    [Key("quantity")] public int quantity;
    [Key("lastObtainedAt")] public long lastObtainedAt;
}
