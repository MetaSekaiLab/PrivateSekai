using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserInherit
{
    [Key("inheritId")] public string? inheritId;
}
