using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCheerfulCarnival
{
    [Key("eventId")] public int eventId;
    [Key("cheerfulCarnivalTeamId")] public int cheerfulCarnivalTeamId;
    [Key("teamChangeCount")] public int teamChangeCount;
}
