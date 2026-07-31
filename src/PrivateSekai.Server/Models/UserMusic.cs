using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMusic
{
    [Key("musicId")] public int musicId;
}
