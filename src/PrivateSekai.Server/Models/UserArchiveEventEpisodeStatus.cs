using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserArchiveEventEpisodeStatus
{
    [Key("storyType")] public string? storyType;
    [Key("episodeId")] public int episodeId;
    [Key("status")] public string? status;
    [Key("isNotSkipped")] public bool isNotSkipped;
}
