using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCustomProfile
{
    [Key("customProfileId")] public int customProfileId;
    [Key("name")] public string? name;
}
