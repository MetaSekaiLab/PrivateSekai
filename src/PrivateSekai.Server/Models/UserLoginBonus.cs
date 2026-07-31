using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserLoginBonus
{
    public const string LOGINBONUS_TYPE_NORMAL = "normal";
    public const string LOGINBONUS_TYPE_BEGINNER = "beginner";
    public const string LOGINBONUS_TYPE_LIMITED = "limited";

    [Key("userId")] public long userId;
    [Key("loginBonusId")] public int loginBonusId;
    [Key("loginBonusType")] public string? loginBonusType;
    [Key("progress")] public int progress;
    [Key("receivedAt")] public long receivedAt;
    [Key("displayTexts")] public string[]? displayTexts;
}
