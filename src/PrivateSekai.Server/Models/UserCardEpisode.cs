using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCardEpisode
{
    [Key("cardEpisodeId")] public int cardEpisodeId;
    [Key("scenarioStatus")] public string? scenarioStatus;
    [Key("scenarioStatusReasons")] public string[]? scenarioStatusReasons;
    [Key("isNotSkipped")] public bool isNotSkipped;
}
