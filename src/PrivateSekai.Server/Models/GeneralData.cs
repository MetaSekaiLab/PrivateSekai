using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class GeneralData
{
    [Key("objectData")] public ObjectData? objectData;
    [Key("type")] public int playerInfoResourceId;
}
