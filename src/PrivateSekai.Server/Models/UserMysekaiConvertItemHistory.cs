using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiConvertItemHistory
{
    [Key("mysekaiConvertItemHistoryId")] public int mysekaiConvertItemHistoryId;
    [Key("obtainedAt")] public long obtainedAt;
}
