using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCustomProfileCard
{
    [Key("customProfileId")] public int customProfileId;
    [Key("customProfileCardId")] public int customProfileCardId;
    [Key("thumbnailPath")] public string? thumbnailPath;
    [Key("customProfileCard")] public ProfileCardData? customProfileCard;
    [Key("seq")] public int seq;
}
