using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserSkillPracticeTicket
{
    [Key("userId")] public long userId;
    [Key("skillPracticeTicketId")] public int skillPracticeTicketId;
    [Key("quantity")] public int quantity;
}
