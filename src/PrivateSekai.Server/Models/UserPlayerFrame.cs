using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserPlayerFrame
{
    [Key("playerFrameId")] public int playerFrameId;
    [Key("playerFrameAttachStatus")] public string? playerFrameAttachStatus;
}
