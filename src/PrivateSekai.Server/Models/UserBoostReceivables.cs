using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserBoostReceivables
{
    [Key("boostReceivableId")] public string? boostReceivableId;
}
