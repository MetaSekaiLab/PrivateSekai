using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserProfile
{
    [Key("userId")] public long userId;
    [Key("word")] public string? word;
    [Key("twitterId")] public string? twitterId;
    [Key("profileImageType")] public string? profileImageType;
    [Key("profileImageId")] public int profileImageId;
}
