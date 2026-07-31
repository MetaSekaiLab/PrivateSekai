using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCostume3DStatus
{
    [Key("costume3dId")] public int costume3dId;
    [Key("obtainedAt")] public long obtainedAt;
    [Key("status")] public string? status;
}
