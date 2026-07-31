using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserStoryMission
{
    [Key("progress")] public int progress;
}
