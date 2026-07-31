using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class CardData
{
    [Key("objectData")] public ObjectData? objectData;
    [Key("id")] public int id;
    [Key("type")] public int type;
    [Key("showMasterRank")] public bool showMasterRank;
    [Key("useAfterSpecialTraining")] public bool useAfterSpecialTraining;
}
