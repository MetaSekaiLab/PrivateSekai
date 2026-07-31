using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserLoginStatus
{
    [Key("loginStatus")] public string? loginStatus;
    [Key("loginStatusUpdatedAt")] public long loginStatusUpdatedAt;
}
