using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserCustomProfileResource
{
    [Key("customProfileResourceId")] public int customProfileResourceId;
    [Key("customProfileResourceType")] public string? customProfileResourceType;
    [Key("quantity")] public int quantity;
    [Key("characterId")] public int characterId;
}
