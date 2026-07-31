using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCustomProfileResourceUsages
{
    [Key("customProfileId")] public int customProfileId;
    [Key("customProfileResourceId")] public int customProfileResourceId;
    [Key("quantity")] public int quantity;
}
