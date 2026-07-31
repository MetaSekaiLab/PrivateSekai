using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserTutorial
{
    [Key("tutorialStatus")] public string? tutorialStatus;
    [Key("tutorialEndAt")] public long tutorialEndAt;
}
