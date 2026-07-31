using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiCanvasLayout
{
    [Key("position")] public UserMysekaiFixturePosition? position;
    [Key("rotation")] public int rotation;
    [Key("mysekaiFixtureId")] public int mysekaiFixtureId;
    [Key("cardId")] public int cardId;
    [Key("isSpecialTraining")] public bool isSpecialTraining;
    [Key("mysekaiUniqueId")] public string? mysekaiUniqueId;
}
