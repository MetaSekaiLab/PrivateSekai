using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiStamina
{
    [Key("normalStamina")] public int normalStamina;
    [Key("enhanceStamina")] public int enhanceStamina;
    [Key("boostStamina")] public int boostStamina;
}
