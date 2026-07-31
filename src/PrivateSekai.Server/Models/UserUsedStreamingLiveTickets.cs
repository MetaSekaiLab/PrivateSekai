using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserUsedStreamingLiveTickets
{
    [Key("virtualLiveTicketId")] public int virtualLiveTicketId;
    [Key("virtualLiveId")] public int virtualLiveId;
    [Key("virtualLiveScheduleId")] public int virtualLiveScheduleId;
}
