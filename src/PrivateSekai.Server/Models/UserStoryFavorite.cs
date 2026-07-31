using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserStoryFavorite
{
    [Key("shareNo")] public int shareNo;
    [Key("storyType")] public string? storyType;
    [Key("storyId")] public int storyId;
    [Key("comment")] public string? comment;
    [Key("isSpoiler")] public bool isSpoiler;
}
