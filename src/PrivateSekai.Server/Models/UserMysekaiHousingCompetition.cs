using MessagePack;

namespace PrivateSekai.Models;

[MessagePackObject]
public class UserMysekaiHousingCompetition
{
    [Key("mysekaiHousingCompetitionId")] public int mysekaiHousingCompetitionId;
    [Key("name")] public string? name;
    [Key("word")] public string? word;
    [Key("thumbnailPath")] public string? thumbnailPath;
    [Key("mysekaiHousingCompetitionSubmissionScope")] public string? mysekaiHousingCompetitionSubmissionScope;
    [Key("mysekaiHousingCompetitionSearchId")] public string? mysekaiHousingCompetitionSearchId;
}
