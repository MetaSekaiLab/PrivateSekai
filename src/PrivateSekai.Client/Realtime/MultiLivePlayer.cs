extern alias game;

using game::Sekai;
using game::Sekai.MultiLive;
using PrivateSekai.Config;
using PrivateSekai.Shared.Master;

namespace PrivateSekai.Client.Realtime;

public sealed class MultiLivePlayer(string directory)
{
    private readonly MasterData master = new(new MasterCacheConfig(), directory);
    private readonly LiveDeckPower power = new(directory);

    public RoomUserBasicInfo Create(SuiteUser user, long now)
    {
        using var scope = master.BeginRequest();
        var deck = user.userDecks.Single(d => d.deckId == user.userGamedata.deck);
        var ids = new[] { deck.member1, deck.member2, deck.member3, deck.member4, deck.member5 };
        if (ids.Length != ids.Distinct().Count() || !ids.Contains(deck.leader))
            throw new InvalidDataException("主编队成员或队长无效。");
        var owned = user.userCards.ToDictionary(c => c.cardId);
        var definitions = master.GetTable<MasterCard>("cards", c => c.id);
        var leader = owned[deck.leader];
        var card = definitions.FindById(leader.cardId) ?? throw new InvalidDataException("队长卡牌缺少 master。");
        var character = master.GetTable<MasterGameCharacter>("gameCharacters", c => c.id).FindById(card.characterId)
            ?? throw new InvalidDataException("队长角色缺少 master。");
        var costumeUnit = character.unit;
        if (character.supportUnitType == "full")
        {
            var fixedUnit = user.userFixCostumes?.FirstOrDefault(c => c.characterId == character.id)?.unit;
            costumeUnit = !string.IsNullOrEmpty(fixedUnit) && fixedUnit != "none" ? fixedUnit
                : !string.IsNullOrEmpty(card.supportUnit) && card.supportUnit != "none" ? card.supportUnit : character.unit;
        }
        var costume = user.userCharacterCostume3ds.Single(c => c.characterId == character.id && c.unit == costumeUnit);
        var costumes = master.GetTable<MasterCostume3D>("costume3ds", c => c.id);
        foreach (var id in new[] { costume.hairCostume3dId, costume.bodyCostume3dId, costume.headCostume3dId })
            if (costumes.FindById(id) == null) throw new InvalidDataException("穿戴服装缺少 master。");
        var subs = ids.Where(id => id != deck.leader).Select(id => owned[id]).ToArray();
        var frame = user.userPlayerFrames?.FirstOrDefault(f => f.playerFrameAttachStatus == "first");
        return new RoomUserBasicInfo
        {
            CardId = leader.cardId, CardLevel = leader.level, CardSkillLv = leader.skillLevel, CardMasterRank = leader.masterRank,
            SubCardIds = subs.Select(c => c.cardId).ToArray(), SubCardSkillLv = subs.Select(c => c.skillLevel).ToArray(),
            SubCardImages = subs.Select(c => c.defaultImage).ToArray(), TotalPowerIncludeBuff = power.Calculate(user, now),
            DefaultImage = leader.defaultImage, IsTraining = leader.specialTrainingStatus == "done",
            MainHonor = Honor(user, 1), SubHonors = [Honor(user, 2), Honor(user, 3)],
            CostumeUnitType = Enum.TryParse<UnitType>(costume.unit, out var unit) ? unit : UnitType.none,
            HairCostumeId = costume.hairCostume3dId, BodyCostumeId = costume.bodyCostume3dId, AccessoryCostumeId = costume.headCostume3dId,
            friendRequestStatus = Enum.TryParse<FriendRequestStatus>(user.userConfig.friendRequestScope, out var friend) ? friend : default,
            MemberCharacterRank = ids.Select(id =>
            {
                var member = definitions.FindById(id) ?? throw new InvalidDataException("成员卡牌缺少 master。");
                return new MemberCharacterRank
                {
                    characterId = member.characterId,
                    characterRank = user.userCharacters.Single(c => c.characterId == member.characterId).characterRank
                };
            }).ToArray(),
            UserPlayerFrameId = frame?.playerFrameId ?? 0, UserPlayerFramePartsLayout = frame?.partsLayout
        };
    }

    private RoomUserHonorInfo Honor(SuiteUser user, int slot)
    {
        var profile = user.userProfileHonors?.SingleOrDefault(h => h.seq == slot);
        if (profile == null) return new RoomUserHonorInfo { Type = 1 };
        var type = Enum.TryParse<HonorType>(profile.profileHonorType, out var parsed) ? parsed : HonorType.empty;
        var progress = 0;
        if (type != HonorType.bonds && user.userHonorMissions is { Length: > 0 })
        {
            var honor = master.GetTable<MasterHonor>("honors", h => h.id).FindById(profile.honorId)
                ?? throw new InvalidDataException("穿戴称号缺少 master。");
            progress = user.userHonorMissions.FirstOrDefault(m => m.honorMissionType == honor.honorMissionType)?.progress ?? 0;
        }
        return new RoomUserHonorInfo
        {
            Type = (int)type, Id = profile.honorId, Level = profile.honorLevel, BondsWordId = profile.bondsHonorWordId,
            BondsViewType = Enum.TryParse<HonorViewType>(profile.bondsHonorViewType, out var view) ? (int)view : (int)HonorViewType.normal,
            HonorMissionProgress = progress, HonorBackgroundId = profile.honorBackgroundId.GetValueOrDefault(),
            HonorWordId = profile.honorWordId.GetValueOrDefault()
        };
    }
}
