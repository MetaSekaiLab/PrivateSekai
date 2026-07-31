using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGachaWish
{
    [Key("gachaId")] public int gachaId;
    [Key("gachaDetailId")] public int gachaDetailId;
}
