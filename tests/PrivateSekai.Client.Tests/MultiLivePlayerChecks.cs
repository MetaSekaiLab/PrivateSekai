extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client.Realtime;
using PrivateSekai.Protocol;

internal static class MultiLivePlayerChecks
{
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "../../../obj/fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Write("cards", Enumerable.Range(1, 5).Select(id => new
        {
            id, characterId = id, cardRarityType = "rarity_2", supportUnit = "none", attr = "cool",
            specialTrainingPower1BonusFixed = 20, specialTrainingPower2BonusFixed = 30, specialTrainingPower3BonusFixed = 40,
            cardParameters = new[]
            {
                new { cardParameterType = "param1", cardLevel = 2, power = 150 },
                new { cardParameterType = "param2", cardLevel = 2, power = 260 },
                new { cardParameterType = "param3", cardLevel = 2, power = 370 },
                new { cardParameterType = "param1", cardLevel = 1, power = 100 },
                new { cardParameterType = "param2", cardLevel = 1, power = 200 },
                new { cardParameterType = "param3", cardLevel = 1, power = 300 }
            }
        }));
        Write("gameCharacters", Enumerable.Range(1, 5).Select(id => new { id, unit = "light_sound", supportUnitType = "none" }));
        Write("characterRanks", Enumerable.Range(1, 5).Select(id => new
        {
            characterId = id, characterRank = 1, power1BonusRate = 0, power2BonusRate = 0, power3BonusRate = 0
        }).Append(new { characterId = 1, characterRank = 2, power1BonusRate = 10, power2BonusRate = 20, power3BonusRate = 30 }));
        Raw("masterLessons", """
            [{"cardRarityType":"rarity_2","masterRank":2,"power1BonusFixed":7,"power2BonusFixed":11,"power3BonusFixed":13},
             {"cardRarityType":"rarity_2","masterRank":1,"power1BonusFixed":2,"power2BonusFixed":3,"power3BonusFixed":5}]
            """);
        Raw("cardEpisodes", """[{"id":71,"power1BonusFixed":3,"power2BonusFixed":4,"power3BonusFixed":5}]""");
        Raw("cardMysekaiCanvasBonuses", """[{"cardRarityType":"rarity_2","power1BonusFixed":1,"power2BonusFixed":2,"power3BonusFixed":3}]""");
        Raw("honors", """
            [{"id":81,"honorMissionType":"fixture","levels":[{"level":1,"bonus":3},{"level":2,"bonus":7}]},
             {"id":82,"levels":[{"level":1,"bonus":10}]}]
            """);
        Raw("areaItemLevels", """
            [{"areaItemId":91,"level":1,"targetUnit":"any","targetCardAttr":"any","power1BonusRate":10,"power2BonusRate":10,"power3BonusRate":10},
             {"areaItemId":91,"level":2,"targetUnit":"any","targetCardAttr":"any","power1BonusRate":99,"power2BonusRate":99,"power3BonusRate":99}]
            """);
        Raw("mysekaiGates", """[{"id":101,"unit":"light_sound"},{"id":102,"unit":"idol"}]""");
        Raw("mysekaiGateLevels", """
            [{"mysekaiGateId":101,"level":1,"powerBonusRate":10},
             {"mysekaiGateId":102,"level":2,"powerBonusRate":20}]
            """);
        Raw("mysekaiFixtureGameCharacterPerformanceBonusLimit", """{"id":1,"bonusRateLimit":100}""");
        Raw("configs", """[{"configKey":"mysekai_fixture_game_character_bonus_rate_scale","value":"100"}]""");
        Raw("events", """[{"id":111,"eventOnlyComponentDisplayStartAt":1000,"startAt":2000,"aggregateAt":4000}]""");
        Raw("eventTotalPowerLimits", """[{"eventId":111,"upperTotalPower":2500}]""");
        Raw("eventMysekaiFixtureGameCharacterPerformanceBonusLimits", """[{"eventId":111,"bonusRateLimit":50}]""");
        Raw("costume3ds", """[{"id":201},{"id":202},{"id":203}]""");
        var original = JsonNode.Parse("""
            {"userGamedata":{"deck":7},"userConfig":{"friendRequestScope":"id_search"},
             "userDecks":[{"deckId":7,"leader":3,"member1":1,"member2":2,"member3":3,"member4":4,"member5":5,"member6":999}],
             "userCards":[{"cardId":1,"level":1,"skillLevel":1,"defaultImage":"original"},
                          {"cardId":2,"level":1,"skillLevel":2,"defaultImage":"special_training"},
                          {"cardId":3,"level":1,"skillLevel":3,"defaultImage":"original"},
                          {"cardId":4,"level":1,"skillLevel":4,"defaultImage":"original"},
                          {"cardId":5,"level":1,"skillLevel":1,"defaultImage":"original"}],
             "userCharacters":[{"characterId":1,"characterRank":1},{"characterId":2,"characterRank":1},
                               {"characterId":3,"characterRank":1},{"characterId":4,"characterRank":1},{"characterId":5,"characterRank":1}],
             "userCharacterCostume3ds":[{"characterId":3,"unit":"light_sound","hairCostume3dId":201,"bodyCostume3dId":202,"headCostume3dId":203}]}
            """)!.AsObject();
        var calculator = new LiveDeckPower(directory);
        check(Power() == 3000, "当前编队总力累计五人，排除第六 MV 槽位");
        CheckPower(u => u["userCards"]![0]!["level"] = 2, 3180, "卡牌参数按等级排序后逐等级取值，不插值");
        CheckPower(u => u["userCards"]![0]!["specialTrainingStatus"] = "done", 3090, "特训增加三项固定属性");
        CheckPower(u => u["userCards"]![0]!["masterRank"] = 2, 3041, "Master Rank 按排序后的前 N 项累计");
        CheckPower(u => u["userCards"]![0]!["episodes"] = JsonNode.Parse("""
            [{"cardEpisodeId":71,"scenarioStatus":"already_read","isSkipped":true},
             {"cardEpisodeId":71,"scenarioStatus":"unread"},{"cardEpisodeId":999,"scenarioStatus":"already_read"}]
            """), 3012, "仅已读剧情增加属性，缺失 master 不凭空增加");
        CheckPower(u => u["userMysekaiCanvases"] = JsonNode.Parse("""[{"cardId":1},{"cardId":1}]"""), 3006,
            "Canvas 按持有与稀有度增加一次，不按数量叠加");
        CheckPower(u => u["userAreas"] = JsonNode.Parse("""[{"areaItems":[{"areaItemId":91,"level":1}]}]"""), 3300,
            "区域加成只使用实际持有的精确等级");
        CheckPower(u => u["userCharacters"]![0]!["characterRank"] = 2, 3140, "角色等级分别计算三项加成并向下取整");
        CheckPower(u => u["userHonors"] = JsonNode.Parse("""[{"honorId":81,"level":2},{"honorId":82,"level":1}]"""), 3017,
            "所有持有称号按精确等级计入，不要求穿戴且不累计早期等级");
        CheckPower(u => u["userMysekaiGates"] = JsonNode.Parse("""
            [{"mysekaiGateId":101,"mysekaiGateLevel":1},{"mysekaiGateId":102,"mysekaiGateLevel":2}]
            """), 3300, "普通角色只使用本组合对应的门，不选其他组合最高门");
        var fixture = original.DeepClone().AsObject();
        fixture["userMysekaiFixtureGameCharacterPerformanceBonuses"] = JsonNode.Parse("""[{"gameCharacterId":1,"totalBonusRate":500}]""");
        check(Power(fixture, 999) == 3006 && Power(fixture, 1000) == 3003 && Power(fixture, 4000) == 3006,
            "家具倍率取普通与可显示活动的较小上限，活动结束边界还原普通上限");
        check(Power(now: 1999) == 3000 && Power(now: 2000) == 2500 && Power(now: 3999) == 2500 && Power(now: 4000) == 3000,
            "活动总力限制包含开始、不包含结算时间，预告期间不限制总力");

        var builder = new MultiLivePlayer(directory);
        var info = builder.Create(User(original), 0);
        check(info.CardId == 3 && info.CardSkillLv == 3 && info.TotalPowerIncludeBuff == 3000
            && info.SubCardIds.SequenceEqual(new[] { 1, 2, 4, 5 }) && info.SubCardSkillLv.SequenceEqual(new[] { 1, 2, 4, 1 }),
            "入房使用实际队长，子卡按原编队顺序排除队长");
        check(info.SubCardImages[1] == "special_training" && info.MemberCharacterRank.Select(c => c.characterId).SequenceEqual(new[] { 1, 2, 3, 4, 5 })
            && info.HairCostumeId == 201 && info.BodyCostumeId == 202 && info.AccessoryCostumeId == 203 && (int)info.friendRequestStatus == 1,
            "入房携带实际卡图、五人角色等级、三件服装和好友配置");
        check(info.MainHonor.Type == 1 && info.SubHonors.Length == 2 && info.SubHonors.All(h => h.Type == 1)
            && info.UserPlayerFrameId == 0 && info.UserPlayerFramePartsLayout == null, "未穿戴称号与玩家框保留客户端默认结构");
        var decorated = original.DeepClone().AsObject();
        decorated["userProfileHonors"] = JsonNode.Parse("""
            [{"seq":1,"profileHonorType":"normal","honorId":81,"honorLevel":2,"honorBackgroundId":7,"honorWordId":8},
             {"seq":2,"profileHonorType":"bonds","honorId":555,"honorLevel":3,"bondsHonorWordId":9,"bondsHonorViewType":"reverse"}]
            """);
        decorated["userHonorMissions"] = JsonNode.Parse("""[{"honorMissionType":"fixture","progress":37}]""");
        decorated["userPlayerFrames"] = JsonNode.Parse("""[{"playerFrameId":777,"playerFrameAttachStatus":"first","partsLayout":[]}]""");
        var dressed = builder.Create(User(decorated), 0);
        check(dressed.MainHonor.HonorMissionProgress == 37 && dressed.MainHonor.HonorBackgroundId == 7 && dressed.MainHonor.HonorWordId == 8
            && dressed.SubHonors[0].Type == 2 && dressed.SubHonors[0].BondsViewType == 4 && dressed.SubHonors[0].HonorMissionProgress == 0
            && dressed.UserPlayerFrameId == 777, "普通称号携带关联任务，羁绊称号保留词条及视图，玩家框取实际穿戴");
        var invalid = original.DeepClone().AsObject();
        invalid["userDecks"]![0]!["leader"] = 99;
        Reject(() => builder.Create(User(invalid), 0));
        invalid["userDecks"]![0]!["leader"] = 3;
        invalid["userDecks"]![0]!["member5"] = 1;
        Reject(() => builder.Create(User(invalid), 0));
        var before = original.ToJsonString();
        builder.Create(User(original), 0);
        check(original.ToJsonString() == before, "资料构建与战力计算不改变输入状态");

        var characters = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "gameCharacters.json")))!.AsArray();
        characters[2]!["unit"] = "piapro";
        characters[2]!["supportUnitType"] = "full";
        Raw("gameCharacters", characters.ToJsonString());
        Raw("mysekaiGateLevels", """
            [{"mysekaiGateId":101,"level":1,"powerBonusRate":10},
             {"mysekaiGateId":102,"level":2,"powerBonusRate":5}]
            """);
        var singer = original.DeepClone().AsObject();
        singer["userMysekaiGates"] = JsonNode.Parse("""
            [{"mysekaiGateId":101,"mysekaiGateLevel":1},{"mysekaiGateId":102,"mysekaiGateLevel":2}]
            """);
        check(new LiveDeckPower(directory).Calculate(User(singer), 0) == 3270,
            "无支援组合的 piapro 按最高持有门等级选择，不比较门倍率大小");
        var cards = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "cards.json")))!.AsArray();
        cards[2]!["supportUnit"] = "light_sound";
        Raw("cards", cards.ToJsonString());
        check(new LiveDeckPower(directory).Calculate(User(singer), 0) == 3300,
            "有支援组合时使用对应组合的门，覆盖 piapro 最高等级分支");
        cards[2]!["supportUnit"] = "idol";
        Raw("cards", cards.ToJsonString());
        singer["userCharacterCostume3ds"] = JsonNode.Parse("""
            [{"characterId":3,"unit":"piapro","hairCostume3dId":201,"bodyCostume3dId":202,"headCostume3dId":203},
             {"characterId":3,"unit":"idol","hairCostume3dId":203,"bodyCostume3dId":201,"headCostume3dId":202},
             {"characterId":3,"unit":"light_sound","hairCostume3dId":202,"bodyCostume3dId":203,"headCostume3dId":201}]
            """);
        var supported = new MultiLivePlayer(directory).Create(User(singer), 0);
        check(supported.CostumeUnitType.ToString() == "idol" && supported.HairCostumeId == 203,
            "full 支援角色未固定组合时按卡牌支援选择服装");
        singer["userFixCostumes"] = JsonNode.Parse("""[{"characterId":3,"unit":"light_sound"}]""");
        supported = new MultiLivePlayer(directory).Create(User(singer), 0);
        check(supported.CostumeUnitType.ToString() == "light_sound" && supported.HairCostumeId == 202,
            "full 支援角色固定服装组合优先于卡牌支援组合");
        characters[2]!["supportUnitType"] = "unit";
        Raw("gameCharacters", characters.ToJsonString());
        supported = new MultiLivePlayer(directory).Create(User(singer), 0);
        check(supported.CostumeUnitType.ToString() == "piapro" && supported.HairCostumeId == 201,
            "非 full 支援角色使用角色自身组合的穿戴，不套用固定组合分支");

        int Power(JsonObject? value = null, long now = 0) => calculator.Calculate(User(value ?? original), now);
        void CheckPower(Action<JsonObject> change, int expected, string label)
        {
            var value = original.DeepClone().AsObject();
            change(value);
            check(Power(value) == expected, label);
        }
        void Reject(Action action)
        {
            var rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "队长或成员无效时在发包前拒绝");
        }
        void Write(string name, object value) => File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value));
        void Raw(string name, string value) => File.WriteAllText(Path.Combine(directory, name + ".json"), value);
        static SuiteUser User(JsonObject value) => JsonSerializer.Deserialize<SuiteUser>(value.ToJsonString(), DumpJson.Options)!;
    }
}
