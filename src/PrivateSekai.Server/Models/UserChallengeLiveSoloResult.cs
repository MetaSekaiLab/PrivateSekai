using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserChallengeLiveSoloResult
{
    [Key("characterId")] public int characterId;
    [Key("highScore")] public int highScore;
}
