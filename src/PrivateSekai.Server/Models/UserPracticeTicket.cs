using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPracticeTicket
{
    [Key("userId")] public long userId;
    [Key("practiceTicketId")] public int practiceTicketId;
    [Key("quantity")] public int quantity;
}
