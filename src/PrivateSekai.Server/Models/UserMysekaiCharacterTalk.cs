using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiCharacterTalk
{
    [Key("mysekaiCharacterTalkId")] public int mysekaiCharacterTalkId;
    [Key("isRead")] public bool isRead;
}
