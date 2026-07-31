using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserAutoLive
{
    [Key("count")] public int count;
}
