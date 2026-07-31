using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiReleaseElement
{
    [Key("mysekaiReleaseElementType")] public string? mysekaiReleaseElementType;
}
