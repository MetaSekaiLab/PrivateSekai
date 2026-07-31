using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserVirtualLiveTransitionItem
{
    [Key("virtualLiveTransitionItemId")] public int virtualLiveTransitionItemId;
    [Key("quantity")] public int quantity;
}
