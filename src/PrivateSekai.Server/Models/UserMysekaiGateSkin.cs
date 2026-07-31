using System.Collections.Generic;
using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiGateSkin
{
    [Key("mysekaiGateSkinIds")] public List<int>? mysekaiGateSkinIds;
}
