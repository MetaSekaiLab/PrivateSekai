using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMusicMyList
{
    [Key("listNo")] public int listNo;
    [Key("name")] public string? name;
    [Key("musicIds")] public int[]? musicIds;
}
