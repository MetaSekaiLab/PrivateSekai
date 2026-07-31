using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPlatforms
{
    [Key("provider")] public string? provider;
}
