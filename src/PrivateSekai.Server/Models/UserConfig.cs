using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserConfig
{
    [Key("defaultMusicType")] public string? defaultMusicType;
    [Key("isDisplayLoginStatus")] public bool isDisplayLoginStatus;
    [Key("friendRequestScope")] public string? friendRequestScope;
}
