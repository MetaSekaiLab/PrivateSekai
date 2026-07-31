using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class TextData
{
    [Key("objectData")] public ObjectData? objectData;
    [Key("text")] public string? text;
    [Key("fontId")] public int fontId;
    [Key("type")] public int type;
    [Key("colorId")] public int colorId;
    [Key("size")] public float size;
    [Key("outlineColorId")] public int outlineColorId;
    [Key("outlineSize")] public float outlineSize;
    [Key("lineSpacing")] public float lineSpacing;
}
