using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserWorldBloom
{
    [Key("eventId")] public int eventId;
    [Key("gameCharacterId")] public int gameCharacterId;
    [Key("worldBloomChapterPoint")] public int worldBloomChapterPoint;
    [Key("worldBloomChapterPointUpdateAt")] public long worldBloomChapterPointUpdateAt;
    [Key("rank")] public int rank;
}
