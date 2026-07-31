using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class PaidUnitPrice
{
    [Key("remaining")] public int remaining;
    [Key("unitPrice")] public int unitPrice;
}
