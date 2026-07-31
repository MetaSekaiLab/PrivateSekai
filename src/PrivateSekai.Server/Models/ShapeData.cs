using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class ShapeData
{
    [Key("objectData")] public ObjectData? objectData;
    [Key("id")] public int id;
    [Key("colorId")] public int colorId;
    [Key("outlineColorId")] public int outlineColorId;
    [Key("alpha")] public float alpha;
    [Key("outlineAlpha")] public float outlineAlpha;
    [Key("outlineSize")] public float outlineSize;
}
