extern alias game;

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using game::Sekai;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class GachaBonusChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "gacha-bonus-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        var gachas = JsonNode.Parse("""
            [{"id":1,"startAt":1,"gachaBonusId":2,"gachaCeilItemId":9,
              "gachaDetails":[{"cardId":2,"weight":1},{"cardId":3,"weight":1},{"cardId":4,"weight":9,"fixedBonusWeight":0},{"cardId":5,"weight":1,"fixedBonusWeight":1}],
              "gachaPickups":[{"cardId":4,"gachaPickupType":"normal"},{"cardId":5,"gachaPickupType":"normal"}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100},{"cardRarityType":"rarity_3","lotteryType":"normal","rate":1}],
              "gachaBehaviors":[{"id":1,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300},
                {"id":2,"spinCount":1,"costResourceType":"paid_jewel","costResourceQuantity":300},
                {"id":3,"spinCount":1,"costResourceType":"gacha_ticket","costResourceId":1,"costResourceQuantity":1},
                {"id":4,"spinCount":10,"gachaBehaviorType":"over_rarity_3_once","costResourceType":"jewel","costResourceQuantity":3000},
                {"id":5,"spinCount":1,"resourceCategory":"free_resource","gachaSpinnableType":"colorful_pass"}]}]
            """)!.AsArray();
        foreach (var (id, bonusId) in new[] { (2, 1), (3, 2), (4, 0) })
        {
            var copy = gachas[0]!.DeepClone();
            copy["id"] = id;
            copy["gachaBonusId"] = bonusId;
            gachas.Add(copy);
        }
        Table("gachas", gachas.ToJsonString());
        Table("gachaBonuses", """[{"id":1,"switchFixedBonusWeight":false},{"id":2,"switchFixedBonusWeight":true}]""");
        Table("clientConfigs", """[{"id":113,"type":"Float","value":"0.5"},{"id":114,"type":"Float","value":"1.0"}]""");
        Table("colorfulPassV2s", """[{"id":3,"dailyFreeGachaSpinLimit":1,"dailyPaidGachaSpinLimit":2,"weeklyColorfulPassGachaSpinLimit":1}]""");
        Table("cards", """[{"id":2,"characterId":1,"cardRarityType":"rarity_2"},{"id":3,"characterId":1,"cardRarityType":"rarity_3"},{"id":4,"characterId":1,"cardRarityType":"rarity_4"},{"id":5,"characterId":1,"cardRarityType":"rarity_4"}]""");
        Table("cardEpisodes", """[{"id":50,"cardId":5},{"id":51,"cardId":5}]""");
        Table("cardExtras", "[]");
        Table("cardCostume3ds", """[{"cardId":5,"costume3dId":10},{"cardId":5,"costume3dId":11},{"cardId":5,"costume3dId":12},{"cardId":5,"costume3dId":13},{"cardId":5,"costume3dId":14,"isInitialObtainHair":true}]""");
        Table("costume3ds", """[{"id":10,"characterId":1,"partType":"head"},{"id":11,"characterId":1,"partType":"body"},{"id":12,"characterId":1,"partType":"head"},{"id":13,"characterId":1,"partType":"hair"},{"id":14,"characterId":1,"partType":"hair"}]""");
        Table("costume3dShopItems", """[{"id":21,"headCostume3dId":20,"bodyCostume3dId":21,"releaseConditionId":10},{"id":22,"bodyCostume3dId":22,"releaseConditionId":11}]""");
        Table("releaseConditions", """[{"id":10,"releaseConditionType":"master_rank","releaseConditionTypeId":5,"releaseConditionTypeLevel":0},{"id":11,"releaseConditionType":"master_rank","releaseConditionTypeId":5,"releaseConditionTypeLevel":2}]""");
        Table("characterMissionV2s", """[{"id":1,"characterId":1,"characterMissionType":"collect_member","parameterGroupId":1},{"id":2,"characterId":1,"characterMissionType":"waiting_room","parameterGroupId":1},{"id":3,"characterId":1,"characterMissionType":"collect_character_archive_voice","parameterGroupId":1},{"id":4,"characterId":1,"characterMissionType":"collect_costume_3d","parameterGroupId":1}]""");
        Table("characterMissionV2ParameterGroups", """[{"id":1,"seq":1,"requirement":1}]""");
        Table("characterArchiveVoices", """[{"id":1,"groupId":1,"gameCharacterId":1,"characterArchiveVoiceType":"collect_card","externalId":5}]""");
        Table("honorMissions", """[{"id":1,"honorMissionType":"collect_costume_3d","requirement":1},{"id":2,"honorMissionType":"duplicate_card_total","requirement":1}]""");
        Table("beginnerMissionV2s", """[{"id":1,"beginnerMissionV2Type":"make_any_costume","requirement":1}]""");
        var store = new MemoryUserStore();
        var random = new DrawRandom();
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).AddSingleton<Random>(random).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var user = scope.ServiceProvider.GetRequiredService<UserSession>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        store.Save(2, TestUsers.Create(2));
        var peer = DumpSerializer.Serialize(store.Read(2)!.Data);
        void Reset(int free = 6000, int paid = 0, float point = 0, float total = 0, bool existing = true, int pool = 1)
        {
            var state = TestUsers.Create(1);
            state.Data.userChargedCurrency = new() { free = free, paid = paid };
            state.Data.userGachaTickets = [new() { gachaTicketId = 1, quantity = 1 }];
            state.Data.userColorfulPassV2 = new() { colorfulPassId = 3, expiredAt = ulong.MaxValue };
            state.Data.userGachaBonusPoints = existing ? [new() { userId = 1, gachaId = pool, gachaBonusPoint = point, totalGachaBonusPoint = total }] : [];
            state.Data.userCostume3dStatuses = [new() { costume3dId = 10, status = "forbidden" }, new() { costume3dId = 11, status = "forbidden" },
                new() { costume3dId = 12, status = "forbidden" }, new() { costume3dId = 13, status = "forbidden" }, new() { costume3dId = 20, status = "available", obtainedAt = 7 }];
            state.Data.userCostume3dShopItems = [new() { costume3dShopItemId = 21, status = "forbidden" }, new() { costume3dShopItemId = 22, status = "forbidden" }];
            state.Data.refreshableTypes = ["userMaterials"];
            store.Save(1, state);
            random.PickupIndex = 0;
        }
        UserGachaResponse Draw(int behavior = 1, bool paidFirst = false, int pool = 1, int? executions = null) =>
            DumpSerializer.Deserialize<UserGachaResponse>(operation.Execute(1, () =>
            {
                var response = gacha.ExecuteGacha(pool, behavior, paidFirst, executions);
                response.updatedResources = user.BuildRefresh(prepareRefresh: false);
                return response;
            }));
        float Points() => store.Read(1)!.Data.userGachaBonusPoints.Single(p => p.gachaId == 1).gachaBonusPoint;
        foreach (var item in new (int Free, int Paid, bool First, float Expected)[] { (300, 0, false, .5f), (0, 300, false, 1f),
            (150, 150, false, .5f), (150, 150, true, .5f), (300, 300, true, 1f), (300, 300, false, .5f) })
        {
            Reset(item.Free, item.Paid, existing: false);
            var result = Draw(paidFirst: item.First);
            Check.That(Points() == item.Expected && result.updatedResources.userGachaBonusPoints.Single().userId == 1 &&
                result.updatedResources.userGachaBonusPoints.Single().totalGachaBonusPoint == item.Expected,
                "首抽建立所属卡池积分：免费半点、纯付费一点、混合单抽半点，服从扣款优先级");
        }
        Reset(paid: 300);
        Draw(2);
        Check.That(Points() == 1, "付费专用行为按一点积分结算");
        Reset();
        Draw(3);
        Check.That(Points() == .5f && store.Read(1)!.Data.userGachaTickets.Single().quantity == 0, "抽卡券增加半点并扣到零");
        Reset(free: 1350, paid: 1650);
        Draw(4);
        Check.That(Points() == 7.5f, "混合十连仅完整付费的五抽计一点，其余五抽各半点");
        Reset(free: 150, paid: 450, point: 49, total: 149);
        var specified = Draw(executions: 2);
        Check.That(Points() == 50.5f && specified.obtainPrizes.Length == 2 && specified.obtainPrizes[0].gachaLotteryType == "fixed_gacha_bonus" &&
            specified.userGacha.count == 2 && specified.obtainGachaCeilItems.Single().quantity == 2 &&
            store.Read(1)!.Data.userChargedCurrency.free == 0 && store.Read(1)!.Data.userChargedCurrency.paid == 0,
            "指定两抽按总费用识别一抽混合与一抽纯付费，累计一点五分并在原两张名额中完成保底");
        Reset(free: 0, point: 49.5f, total: 149.5f);
        var free = Draw(5);
        Check.That(Points() == 49.5f && free.obtainPrizes.Length == 1 && free.obtainGachaCeilItems.Length == 0 &&
            free.updatedResources.userGachaBonusPoints == null, "通行证免费抽不加积分、不触发保底、不发天井");
        Reset(existing: false);
        Draw(pool: 4);
        Check.That(store.Read(1)!.Data.userGachaBonusPoints.Length == 0, "无Bonus配置的卡池不创建积分");
        Reset(point: 49.5f, total: 149.5f);
        var fifty = Draw();
        var saved = store.Read(1)!.Data;
        Check.That(Points() == 50 && saved.userGachaBonusPoints.Single().totalGachaBonusPoint == 150 &&
            fifty.obtainPrizes.Single().card.resourceId == 5 && fifty.obtainPrizes.Single().gachaLotteryType == "fixed_gacha_bonus",
            "跨50点单抽仅发一张保底四星，使用专门权重及真实保底奖品标签");
        Check.That(fifty.obtainPrizes[0].costume3d.Select(c => c.resourceId).SequenceEqual([10, 11, 12, 14]) &&
            saved.userCostume3dStatuses.Single(c => c.costume3dId == 13).status == "forbidden" &&
            saved.userCostume3dStatuses.Single(c => c.costume3dId == 20).obtainedAt == 7 &&
            saved.userCostume3dStatuses.Single(c => c.costume3dId == 21).status == "sale" &&
            saved.userCostume3dShopItems.Single(c => c.costume3dShopItemId == 21).status == "sale" &&
            saved.userCostume3dShopItems.Single(c => c.costume3dShopItemId == 22).status == "forbidden" &&
            saved.userReleaseConditions.Single().releaseConditionId == 10,
            "保底新卡发放初始头身与许可发型、解锁零MasterRank制作入口，不提前发后续发型或覆盖已有服装");
        Check.That(saved.userCharacterMissions.Single(m => m.characterMissionType == "collect_member").progress == 1 &&
            saved.userCharacterMissions.Single(m => m.characterMissionType == "collect_character_archive_voice").progress == 1 &&
            saved.userCharacterMissions.Single(m => m.characterMissionType == "collect_costume_3d").progress == 1 &&
            saved.userHonorMissions.Single(m => m.honorMissionType == "collect_costume_3d").progress == 1 &&
            (saved.userBeginnerMissionV2s ?? []).All(m => m.beginnerMissionV2Id != 1) && saved.userCards.Single(c => c.cardId == 5).episodes.Length == 2,
            "保底卡联动卡牌、语音、服装收集及称号，头部不多计套数，获得服装不冒充制作");
        Check.That(fifty.obtainGachaCeilItems.Single().quantity == 1 && fifty.userGacha.count == 1 &&
            saved.userChargedCurrency.free == 5700, "保底单抽只扣一次费用、记一次行为并发一个天井");
        Draw();
        Check.That(Points() == 50.5f && store.Read(1)!.Data.userCards.Single(c => c.cardId == 5).duplicateCount == 0,
            "从恰好50点开始不会再次发放该轮50点奖励");
        Reset(point: 49.5f, pool: 2);
        Check.That(Draw(pool: 2).obtainPrizes.Single().card.resourceId == 4, "未开启专门权重的Bonus使用该池普通四星权重");
        foreach (var index in new[] { 0, 1 })
        {
            Reset(point: 99.5f, total: 199.5f);
            random.PickupIndex = index;
            var hundred = Draw();
            Check.That(Points() == 0 && store.Read(1)!.Data.userGachaBonusPoints.Single().totalGachaBonusPoint == 200 &&
                hundred.obtainPrizes.Single().card.resourceId == 4 + index, "100点单抽在普通pickup集合等权选卡，当前积分归零且累计积分保留");
        }
        Reset(point: 99.5f, total: 199.5f);
        var ten = Draw(4);
        Check.That(Points() == 4.5f && ten.obtainPrizes.Length == 10 && ten.obtainPrizes[9].card.resourceId == 3 &&
            ten.obtainPrizes[0].gachaLotteryType == "fixed_gacha_bonus" && ten.obtainGachaCeilItems.Single().quantity == 10,
            "跨100点十连结转超额积分，保底占首位，剩余九张独立执行三星保障，总数仍为十张");
        Reset(point: 49.5f, total: 49.5f);
        var owned = store.Read(1)!;
        owned.Data.userCards = [new() { cardId = 5, level = 30, masterRank = 2, episodes = [] }];
        store.Save(1, owned);
        var duplicate = Draw();
        saved = store.Read(1)!.Data;
        Check.That(!duplicate.obtainPrizes.Single().newFlg && duplicate.obtainPrizes.Single().costume3d.Length == 0 &&
            saved.userCards.Single(c => c.cardId == 5).duplicateCount == 1 && saved.userCards.Single(c => c.cardId == 5).level == 30 &&
            saved.userCharacterMissions.Single(m => m.characterMissionType == "waiting_room").progress == 1 &&
            saved.userHonorMissions.Single(m => m.honorMissionType == "duplicate_card_total").progress == 1,
            "保底重复卡进入候场室并推进重复任务，保留养成且不重复发服装");
        Reset(point: 49.5f, total: 49.5f);
        Draw(pool: 3);
        Check.That(Points() == 49.5f && store.Read(1)!.Data.userGachaBonusPoints.Single(p => p.gachaId == 3).gachaBonusPoint == .5f,
            "不同卡池的积分和保底互不借用");
        Reset(point: 49.5f, total: 49.5f);
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            gacha.ExecuteGacha(1, 1, false);
            return new BrokenResponse();
        }), "保底及服装联动全部执行后响应编码失败");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败回滚积分、所有扣发、服装解锁、任务和待刷新标记");
        Reset(free: 299, point: 49.5f);
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<GachaRequestRejectedException>(() => Draw(), "积分门槛附近仍先拒绝余额不足");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "拒绝不越过积分门槛或发奖");
        Reset(point: 49.5f, total: 149.5f);
        Draw();
        var persisted = store.Read(1)!;
        var path = Path.Combine(directory, "users.db");
        using (var sql = new SqliteUserStore(path)) sql.Save(1, persisted);
        using (var sql = new SqliteUserStore(path))
        {
            var restored = sql.Read(1)!.Data;
            restored.refreshableTypes = persisted.Data.refreshableTypes;
            Check.That(DumpSerializer.Serialize(restored).SequenceEqual(DumpSerializer.Serialize(persisted.Data)), "数据库重启保留积分、保底卡、服装及全部任务状态");
        }
        Check.That(DumpSerializer.Serialize(store.Read(2)!.Data).SequenceEqual(peer), "抽卡积分、保底和异常不修改其他账号");
        foreach (var item in new (string Table, string Json, float Point)[] { ("gachaBonuses", "[]", 49.5f),
            ("clientConfigs", "[]", 49.5f), ("gachas", gachas.ToJsonString().Replace("\"fixedBonusWeight\":1", "\"fixedBonusWeight\":0"), 49.5f),
            ("gachas", gachas.ToJsonString().Replace("\"normal\"", "\"other\""), 99.5f) })
        {
            var original = File.ReadAllText(Path.Combine(directory, item.Table + ".json"));
            Table(item.Table, item.Json);
            using var invalidProvider = new ServiceCollection().AddPrivateSekai()
                .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
                .AddSingleton<IUserStore>(store).AddSingleton<Random>(random).BuildServiceProvider();
            using var invalid = invalidProvider.CreateScope();
            Reset(point: item.Point);
            before = DumpSerializer.Serialize(store.Read(1)!.Data);
            Check.Throws<InvalidOperationException>(() => invalid.ServiceProvider.GetRequiredService<UserOperation>().Execute(1, () =>
                invalid.ServiceProvider.GetRequiredService<GachaService>().ExecuteGacha(1, 1, false)), "缺失Bonus定义、倍率、保底权重或pickup不能伪造奖励");
            Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "错误Bonus master在扣款后失败也整体回滚");
            Table(item.Table, original);
        }
    }

    private sealed class DrawRandom : Random
    {
        public int PickupIndex { get; set; }
        public override double NextDouble() => 0;
        public override long NextInt64(long maxValue) => maxValue == 2 ? PickupIndex : 0;
    }
}
