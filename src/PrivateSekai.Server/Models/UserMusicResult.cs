using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMusicResult
{
    [Key("musicId")] public int musicId;
    [Key("musicDifficultyType")] public string? musicDifficultyType;
    [Key("playType")] public string? playType;
    [Key("playResult")] public string? playResult;
    [Key("highScore")] public int highScore;
    [Key("fullComboFlg")] public bool fullComboFlg;
    [Key("fullPerfectFlg")] public bool fullPerfectFlg;
    [Key("mvpCount")] public int mvpCount;
    [Key("superStarCount")] public int superStarCount;
}
