using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCheerfulCarnivalBehaviours
{
    [Key("eventId")] public int eventId;
    [Key("cheerfulCarnivalBehaviorType")] public string? cheerfulCarnivalBehaviorType;
}
