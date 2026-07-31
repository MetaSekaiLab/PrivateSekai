using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class Boost
{
    [Key("current")] public int current;
    [Key("recoveryAt")] public ulong recoveryAt;
}
