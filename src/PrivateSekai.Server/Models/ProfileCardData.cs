using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class ProfileCardData
{
    [Key("version")] public int version;
    [Key("generals")] public List<GeneralData>? generals;
    [Key("generalBackgrounds")] public List<ImageData>? generalBackgrounds;
    [Key("storyBackgrounds")] public List<ImageData>? storyBackgrounds;
    [Key("standMembers")] public List<ImageData>? standMembers;
    [Key("cardMembers")] public List<CardData>? cardMembers;
    [Key("honors")] public List<HonorData>? honors;
    [Key("bondsHonors")] public List<BondsHonorData>? bondsHonors;
    [Key("texts")] public List<TextData>? texts;
    [Key("collections")] public List<CollectionData>? collections;
    [Key("others")] public List<ImageData>? others;
    [Key("shapes")] public List<ShapeData>? shapes;
    [Key("stamps")] public List<ImageData>? stamps;
}
