using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiBlueprint
{
    [Key("mysekaiBlueprintId")] public int mysekaiBlueprintId;
    [Key("obtainedAt")] public long obtainedAt;
}
