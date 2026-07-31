using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserEventItem
{
    [Key("eventItemId")] public int eventItemId;
    [Key("quantity")] public int quantity;
}
