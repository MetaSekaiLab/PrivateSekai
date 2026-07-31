using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserStreamingLiveTicket
{
    [Key("virtualLiveTicketId")] public int streamingLiveTicketeId;
    [Key("quantity")] public int quantity;
    [Key("totalQuantity")] public int totalQuantity;
}
