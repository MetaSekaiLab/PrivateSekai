using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class ChargedCurrency
{
    [Key("paid")] public int paid;
    [Key("free")] public int free;
    [Key("paidUnitPrices")] public List<PaidUnitPrice>? paidUnitPrices;
}
