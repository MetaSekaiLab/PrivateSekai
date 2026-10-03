using System;

namespace PrivateSekai.Tests;

internal static class Program
{
    public static void Main()
    {
        UserOperationChecks.Run();
        SqliteStoreChecks.Run();
        ArchitectureChecks.Run();
        ResourceTests.Run();
        FeatureChecks.Run();
        BoostRecoveryChecks.Run();
        ChallengeStageChecks.Run();
        ChallengePlayDayChecks.Run();
        ChallengeHighScoreChecks.Run();
        AreaShopChecks.Run();
        SkillPracticeChecks.Run();
        StoryCollectionChecks.Run();
        LoginBonusChecks.Run();
        DeckMissionChecks.Run();
        TransportChecks.Run();
        Console.WriteLine($"通过 {Check.Count} 项检查。");
    }
}
