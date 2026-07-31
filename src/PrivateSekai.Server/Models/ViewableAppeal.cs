using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class ViewableAppeal
{
    [Key("appealIds")] public int[]? appealIds;
}
