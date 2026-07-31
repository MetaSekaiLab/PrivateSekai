using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGachaSelectCharacter
{
    [Key("gachaId")] public int gachaId;
    [Key("gameCharacterId")] public int gameCharacterId;
}
