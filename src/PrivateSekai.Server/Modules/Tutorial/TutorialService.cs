extern alias game;

using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Tutorial;

public sealed class TutorialService(UserSession user, ResourceService resourceService)
{
    private static readonly Dictionary<string, int[]> TutorialCardsByUnit = new()
    {
        ["light_sound_opening"]     = [1, 5, 9, 13, 81, 82, 89, 93, 97, 98, 101, 105],
        ["idol_opening"]            = [17, 21, 25, 29, 81, 83, 89, 90, 93, 97, 101, 105],
        ["street_opening"]          = [33, 37, 41, 45, 81, 84, 89, 93, 94, 97, 102, 105],
        ["theme_park_opening"]      = [49, 53, 57, 61, 81, 85, 89, 93, 97, 101, 105, 106],
        ["school_refusal_opening"]  = [65, 69, 73, 77, 81, 86, 89, 93, 97, 101, 105]
    };

    public void UpdateTutorialProgress(string newStatus)
    {
        if (user.Data.userTutorial == null) return;

        var oldStatus = user.Data.userTutorial.tutorialStatus;
        user.Data.userTutorial.tutorialStatus = newStatus;

        if (oldStatus != null && TutorialCardsByUnit.TryGetValue(oldStatus, out var cardIds))
        {
            foreach (var cardId in cardIds)
            {
                if (user.Data.userCards?.Any(c => c.cardId == cardId) == true)
                    continue;

                resourceService.Grant(new UserResource { resourceType = "card", resourceId = cardId, quantity = 1 });
            }

            user.MarkChanged(new[]
            {
                nameof(SuiteUser.userCards),
                nameof(SuiteUser.userDecks),
                nameof(SuiteUser.userUnitEpisodeStatuses),
                nameof(SuiteUser.userCharacterMissions),
                nameof(SuiteUser.userCharacterMissionStatuses),
                nameof(SuiteUser.userBeginnerMissionV2s),
                nameof(SuiteUser.userMissionStatuses),
                nameof(SuiteUser.userHonorMissions)
            });
        }

        if (newStatus == "end")
        {
            user.Data.userTutorial.tutorialEndAt = user.Now;
        }

        user.MarkChanged(nameof(SuiteUser.userTutorial));
    }
}
