using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBeginnerMissionBehavior
{
    [Key("userBeginnerMissionBehaviorType")] public string? userBeginnerMissionBehaviorType;
}
