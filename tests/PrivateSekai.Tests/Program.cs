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
        StampShopChecks.Run();
        StampFavoriteChecks.Run();
        ProfileHonorChecks.Run();
        MusicMyListChecks.Run();
        VocalShopChecks.Run();
        SkillPracticeChecks.Run();
        StoryCollectionChecks.Run();
        LoginBonusChecks.Run();
        DeckMissionChecks.Run();
        HonorMissionChecks.Run();
        CollectionHonorChecks.Run();
        MusicVideoChecks.Run();
        UserConfigChecks.Run();
        ProfileChecks.Run();
        LoginStatusChecks.Run();
        FriendChecks.Run();
        FriendMissionChecks.Run();
        PairedUserOperationChecks.Run();
        TransportChecks.Run();
        Console.WriteLine($"通过 {Check.Count} 项检查。");
    }
}
