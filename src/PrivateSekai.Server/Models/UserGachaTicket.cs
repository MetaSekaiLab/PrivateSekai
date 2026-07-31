using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserGachaTicket
{
    [Key("userId")] public long userId;
    [Key("gachaTicketId")] public int gachaTicketId;
    [Key("quantity")] public int quantity;
}
