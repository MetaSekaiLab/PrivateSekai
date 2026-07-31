using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiGateCommonInfo
{
    [Key("lastMysekaiGateChangedAt")] public long lastMysekaiGateChangedAt;
    [Key("lastMysekaiGateCharacterReservedAt")] public long lastMysekaiGateCharacterReservedAt;
}
