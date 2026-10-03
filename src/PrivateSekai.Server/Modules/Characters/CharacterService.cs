extern alias game;

using System;
using System.Collections.Generic;
using System.Linq;
using game::Sekai;
using PrivateSekai.Modules.Inventory;
using PrivateSekai.Shared.Resources;
using PrivateSekai.Shared.Users;

namespace PrivateSekai.Modules.Characters;

public sealed class CharacterService(UserSession user, CharacterMasterQueries master, ResourceMasterQueries resourceMaster, ResourceService resources)
{
    public (UpdateExpResult Result, UserResource[] Rewards) Gain(int characterId, int addedExp)
    {
        var character = user.Data.userCharacters?.SingleOrDefault(c => c.characterId == characterId)
            ?? throw new InvalidOperationException("Missing character state.");
        var result = new UpdateExpResult
        {
            beforeLevel = character.characterRank, afterLevel = character.characterRank,
            beforeExp = character.exp, afterExp = character.exp,
            beforeTotalExp = character.totalExp, afterTotalExp = character.totalExp
        };
        if (addedExp == 0) return (result, []);
        var levels = master.GetCharacterLevels();
        var total = checked(character.totalExp + addedExp);
        if (levels.Length == 0 || total >= levels[^1].totalExp)
            throw new NotSupportedException("Character maximum-rank experience is not verified.");
        var level = levels.Last(l => l.totalExp <= total);
        var rewards = new List<UserResource>();
        for (var rank = character.characterRank + 1; rank <= level.level; rank++)
        {
            var row = master.GetCharacterRank(characterId, rank);
            if (row.characterRankAchieveResources?.Length > 0)
                throw new NotSupportedException("Character rank unlock rewards are not verified.");
            foreach (var box in row.rewardResourceBoxIds ?? [])
            {
                var items = resourceMaster.BuildResourcesFromBox("character_rank_reward", box);
                if (items.Length == 0) throw new InvalidOperationException("Missing character rank rewards.");
                rewards.AddRange(items);
            }
        }
        resources.Grant(rewards);
        character.characterRank = result.afterLevel = level.level;
        character.totalExp = result.afterTotalExp = total;
        character.exp = result.afterExp = total - level.totalExp;
        user.MarkChanged(nameof(SuiteUser.userCharacters));
        return (result, rewards.ToArray());
    }

}
