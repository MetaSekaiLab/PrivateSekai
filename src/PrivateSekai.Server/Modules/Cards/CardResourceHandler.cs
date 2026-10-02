extern alias game;

using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Cards;

public sealed class CardResourceHandler(CardMasterQueries master) : IResourceHandler
{
    public IReadOnlyCollection<string> ResourceTypes { get; } = ["card"];

    public void Grant(UserSession user, UserResource resource)
    {
        var cards = user.Data.userCards ?? [];
        var card = cards.FirstOrDefault(c => c.cardId == resource.resourceId);
        if (card != null)
        {
            card.duplicateCount += resource.quantity;
        }
        else
        {
            var episodeIds = master.GetCardEpisodeIds(resource.resourceId);
            card = new UserCard
            {
                userId = user.Data.userGamedata?.userId ?? 0,
                cardId = resource.resourceId,
                level = 1,
                exp = 0,
                totalExp = 0,
                skillLevel = 1,
                skillExp = 0,
                totalSkillExp = 0,
                masterRank = 0,
                specialTrainingStatus = "not_doing",
                defaultImage = "original",
                duplicateCount = resource.quantity - 1,
                createdAt = user.Now,
                episodes =
                [
                    new UserCardEpisode
                    {
                        cardEpisodeId = episodeIds[0],
                        scenarioStatus = "unread_before_scenario",
                        scenarioStatusReasons = [],
                        isNotSkipped = false
                    },
                    new UserCardEpisode
                    {
                        cardEpisodeId = episodeIds[1],
                        scenarioStatus = "can_not_read",
                        scenarioStatusReasons = ["unread_before_scenario", "not_enough_release_condition"],
                        isNotSkipped = false
                    }
                ]
            };
            user.Data.userCards = [.. cards, card];
        }

        user.MarkChanged(nameof(SuiteUser.userCards));
    }
}
