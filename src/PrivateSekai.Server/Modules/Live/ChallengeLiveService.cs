extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Live;

public sealed class ChallengeLiveService(UserSession user, LiveMasterQueries master)
{
    public bool TryUnlockFirstCharacter(int characterId)
    {
        const string behaviorType = "challenge_live_character_force_release";
        var character = master.GetChallengeCharacter(characterId);
        var behaviors = (user.Data.userOneTimeBehaviors ?? []).ToList();
        if (behaviors.Any(b => b.oneTimeBehaviorType == behaviorType) ||
            (user.Data.userGamedata?.rank ?? 0) < master.GetFirstChallengeUnlockRank())
            return false;
        var conditions = (user.Data.userReleaseConditions ?? []).ToList();
        if (conditions.Any(c => c.releaseConditionId == character.orReleaseConditionId))
            return false;
        conditions.Add(new UserReleaseCondition { userId = user.UserId, releaseConditionId = character.orReleaseConditionId, createdAt = user.Now });
        behaviors.Add(new UserOneTimeBehavior { userId = user.UserId, oneTimeBehaviorType = behaviorType });
        user.Data.userReleaseConditions = conditions.OrderBy(c => c.releaseConditionId).ToArray();
        user.Data.userOneTimeBehaviors = behaviors.ToArray();
        user.MarkChanged([nameof(SuiteUser.userReleaseConditions), nameof(SuiteUser.userOneTimeBehaviors)]);
        return true;
    }

    public UserChallengeLiveSoloDeck SaveDeck(int characterId, UserChallengeLiveSoloDeck request)
    {
        if (request.characterId != characterId)
            throw new ArgumentException("Challenge deck character does not match the path.");
        var character = master.GetChallengeCharacter(characterId);
        var released = (user.Data.userReleaseConditions ?? []).Select(c => c.releaseConditionId).ToHashSet();
        if (!released.Contains(character.releaseConditionId) && !released.Contains(character.orReleaseConditionId))
            throw new InvalidOperationException("Challenge character is locked.");
        int?[] slots = [request.leader, request.support1, request.support2, request.support3, request.support4];
        var cards = slots.Where(c => c.HasValue).Select(c => c!.Value).ToArray();
        var owned = (user.Data.userCards ?? []).Select(c => c.cardId).ToHashSet();
        var rank = (user.Data.userCharacters ?? []).SingleOrDefault(c => c.characterId == characterId)?.characterRank ?? 0;
        var limit = master.GetChallengeCardLimit(characterId, rank);
        if (!request.leader.HasValue || cards.Distinct().Count() != cards.Length ||
            cards.Any(c => !owned.Contains(c) || master.GetCardCharacter(c) != characterId) ||
            slots.Skip(limit).Any(c => c.HasValue))
            throw new ArgumentException("Invalid challenge deck members or locked slots.");
        var deck = new UserChallengeLiveSoloDeck
        {
            characterId = characterId, leader = request.leader,
            support1 = request.support1, support2 = request.support2,
            support3 = request.support3, support4 = request.support4
        };
        var decks = (user.Data.userChallengeLiveSoloDecks ?? []).ToList();
        decks.RemoveAll(d => d.characterId == characterId);
        decks.Add(deck);
        user.Data.userChallengeLiveSoloDecks = decks.OrderBy(d => d.characterId).ToArray();
        user.Data.userChallengeLivePlayStatuses ??= [];
        user.Data.userChallengeLiveSoloResults ??= [];
        user.Data.userChallengeLiveSoloStages ??= [];
        user.Data.userChallengeLiveSoloHighScoreRewards ??= [];
        user.MarkChanged([
            nameof(SuiteUser.userChallengeLivePlayDay),
            nameof(SuiteUser.userChallengeLivePlayStatuses), nameof(SuiteUser.userChallengeLiveSoloDecks),
            nameof(SuiteUser.userChallengeLiveSoloResults), nameof(SuiteUser.userChallengeLiveSoloStages),
            nameof(SuiteUser.userChallengeLiveSoloHighScoreRewards)
        ]);
        return deck;
    }
}
