using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBookmarkedStory
{
    [Key("storyId")] public int storyId;
    [Key("storyType")] public string? storyType;
}
