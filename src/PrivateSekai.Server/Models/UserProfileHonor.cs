using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserProfileHonor
{
    [Key("seq")] public int seq;
    [Key("profileHonorType")] public string? profileHonorType;
    [Key("honorId")] public int honorId;
    [Key("bondsHonorViewType")] public string? bondsHonorViewType;
    [Key("bondsHonorWordId")] public int bondsHonorWordId;
    [Key("honorLevel")] public int honorLevel;
}
