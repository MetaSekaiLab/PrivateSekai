extern alias game;

using System;
using System.Linq;
using game::Sekai;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Modules.Characters;

public sealed class CharacterMasterQueries(MasterData master)
{
    public MasterLevel[] GetCharacterLevels() => master.GetTable<MasterLevel>("levels").Rows
        .Where(l => l.levelType == "character").OrderBy(l => l.level).ToArray();

    public MasterCharacterRank GetCharacterRank(int characterId, int rank) =>
        master.GetTable<MasterCharacterRank>("characterRanks").Rows
            .SingleOrDefault(r => r.characterId == characterId && r.characterRank == rank)
        ?? throw new InvalidOperationException("Missing character rank.");

}
