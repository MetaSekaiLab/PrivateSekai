extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Modules.Missions;
using PrivateSekai.Modules.Profiles;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Cards;

public sealed class CardAcquisitionService(UserSession user, CardMasterQueries master, ResourceMasterQueries resourceMaster,
    ResourceService resources, MissionService missions, CostumeService costumes)
{
    public (bool IsNew, UserResource[] Costumes, UserResource[] Extras) Grant(int cardId, int quantity = 1)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var card = master.GetMasterCard(cardId) ?? throw new InvalidOperationException("Missing acquired card definition.");
        var isNew = user.Data.userCards?.All(c => c.cardId != cardId) ?? true;
        resources.Grant(new UserResource { resourceType = "card", resourceId = cardId, resourceLevel = 1, quantity = quantity });
        if (isNew)
        {
            user.Data.userCards = user.Data.userCards!.OrderBy(c => c.cardId).ToArray();
            missions.RecordCardAcquisition(cardId, card.characterId);
        }
        for (var duplicate = isNew ? 1 : 0; duplicate < quantity; duplicate++) missions.RecordDuplicateCard(card.characterId);
        return (isNew, isNew ? costumes.ObtainForCard(cardId) : [], GrantExtras(cardId, quantity));
    }

    private UserResource[] GrantExtras(int cardId, int quantity)
    {
        var box = master.GetCardExtraBox(cardId);
        if (box == null) return [];
        var obtained = new List<UserResource>();
        foreach (var reward in resourceMaster.BuildResourcesFromBox("card_extra", box.id))
        {
            var owned = reward.resourceType switch
            {
                "avatar_accessory" => user.Data.userAvatarAccessories?.Any(a => a.avatarAccessoryId == reward.resourceId) == true,
                "stamp" => user.Data.userStamps?.Any(s => s.stampId == reward.resourceId) == true,
                "mysekai_fixture" => user.Data.userMysekaiFixtures?.Any(f => f.mysekaiFixtureId == reward.resourceId && f.quantity > 0) == true,
                _ => false
            };
            if (owned) continue;
            if (reward.resourceType == "custom_profile_collection_resource") reward.quantity = checked(reward.quantity * quantity);
            resources.Grant(reward);
            if (reward.resourceType == "stamp") missions.RecordStampAcquisition(master.GetStampCharacterId(reward.resourceId));
            obtained.Add(reward);
        }
        return obtained.ToArray();
    }
}
