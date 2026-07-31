using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserVirtualLivePamphlet
{
    [Key("virtualLivePamphletId")] public int virtualLivePamphletId;
    [Key("quantity")] public int quantity;
}
