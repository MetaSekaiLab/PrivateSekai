using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserSerialCodeItem
{
    [Key("serialCodeItemId")] public int serialCodeItemId;
}
