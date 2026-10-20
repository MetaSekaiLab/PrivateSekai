using System.Text.Json.Nodes;
using MessagePack;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class GiftGachaHttpChecks
{
    public static void WriteMaster(string directory)
    {
        foreach (var (table, row) in new[]
        {
            ("gachas", """
                {"id":9870,"gachaType":"gift","startAt":1,"gachaCeilItemId":9870,
                 "gachaDetails":[{"cardId":9874,"weight":1}],
                 "gachaCardRarityRates":[{"cardRarityType":"rarity_4","lotteryType":"normal","rate":100}],
                 "gachaBehaviors":[{"id":9870,"spinCount":10,"executeLimit":3,"gachaBehaviorType":"over_rarity_3_once",
                  "costResourceType":"paid_jewel","costResourceQuantity":1500,"gachaExtraId":9870}]}
                """),
            ("cards", """{"id":9874,"characterId":1,"cardRarityType":"rarity_4"}"""),
            ("gachaExtras", """{"id":9870,"resourceBoxId":9870}"""),
            ("giftGachaExchanges", """{"id":9870,"gachaId":9870,"seq":1,"resourceBoxId":9870}"""),
            ("giftGachaExchanges", """{"id":9871,"gachaId":9870,"seq":2,"resourceBoxId":9871}"""),
            ("giftGachaExchanges", """{"id":9872,"gachaId":9870,"seq":3,"resourceBoxId":9872}"""),
            ("giftGachaExchanges", """{"id":9873,"gachaId":9871,"seq":1,"resourceBoxId":9870}"""),
            ("resourceBoxes", """{"id":9870,"resourceBoxPurpose":"gacha_extra","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":98,"resourceQuantity":2}]}"""),
            ("resourceBoxes", """{"id":9870,"resourceBoxPurpose":"gift_gacha_extra","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":98,"resourceQuantity":3}]}"""),
            ("resourceBoxes", """
                {"id":9871,"resourceBoxPurpose":"gift_gacha_extra","resourceBoxType":"list","details":[
                 {"resourceType":"material","resourceId":98,"resourceQuantity":1},
                 {"resourceType":"practice_ticket","resourceId":9870,"resourceQuantity":2},
                 {"resourceType":"skill_practice_ticket","resourceId":9870,"resourceQuantity":3},
                 {"resourceType":"boost_item","resourceId":9870,"resourceQuantity":4},
                 {"resourceType":"coin","resourceQuantity":5},
                 {"resourceType":"mysekai_material","resourceId":9870,"resourceQuantity":5},
                 {"resourceType":"mysekai_material","resourceId":9871,"resourceQuantity":2}]}
                """),
            ("resourceBoxes", """
                {"id":9872,"resourceBoxPurpose":"gift_gacha_extra","resourceBoxType":"expand","details":[
                 {"resourceType":"material","resourceId":98,"resourceQuantity":1},
                 {"resourceType":"unknown_gift","resourceId":1,"resourceQuantity":1}]}
                """),
            ("mysekaiMaterials", """{"id":9870,"mysekaiMaterialType":"wood"}"""),
            ("mysekaiMaterials", """{"id":9871,"mysekaiMaterialType":"game_character"}""")
        })
        {
            var path = Path.Combine(directory, table + ".json");
            var rows = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : new JsonArray();
            rows.Add(JsonNode.Parse(row));
            JsonFiles.Write(path, rows);
        }
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset()
        {
            var state = original.DeepClone();
            state.Data.userChargedCurrency = new() { paid = 6000 };
            state.Data.userGamedata.coin = 100;
            state.Data.userCards = [];
            state.Data.userGachas = [];
            state.Data.userGiftGachaWishes = [];
            state.Data.userMaterials = [];
            state.Data.userGachaCeilItems = [];
            state.Data.userBoostItems = [];
            state.Data.userPracticeTickets = [];
            state.Data.userSkillPracticeTickets = [];
            state.Data.userMysekaiMaterials = [new() { mysekaiMaterialId = 9870, quantity = 7, lastObtainedAt = 11 }];
            state.Data.userMysekaiMaterialPossession = new() { quantity = 7 };
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Select(int exchange) => new() { Operation = "gacha-gift-wish",
            Body = new JsonObject { ["gachaId"] = 9870, ["giftGachaExchangeId"] = exchange } };
        ScenarioStep Draw() => new() { Operation = "gacha-draw", Args = new()
            { ["gachaId"] = "9870", ["gachaBehaviorId"] = "9870" } };
        Reset();
        var choice = await client.Send(Select(9870));
        var createdAt = choice["updatedResources"]!["userGiftGachaWishes"]![0]!["createdAt"]!.GetValue<long>();
        check(choice["updatedResources"]!["userGiftGachaWishes"]![0]!["giftGachaExchangeId"]!.GetValue<int>() == 9870
            && store.Read(1)!.Data.userChargedCurrency.paid == 6000 && store.Read(1)!.Data.userMaterials.Length == 0,
            "新的Client操作以正确PUT和dump请求模型保存赠品，不收费或提前发奖");
        var first = await client.Send(Draw());
        check(first["obtainGachaExtras"]!.AsArray().Single()!["quantity"]!.GetValue<int>() == 3
            && first["obtainPrizes"]!.AsArray().Count == 10 && store.Read(1)!.Data.userMaterials.Single().quantity == 5,
            "gift十连实际加密HTTP只发一套选定赠品，固定附赠独立叠加且卡牌仍十张");
        await ScenarioRunner.Run(client, new() { Steps = [Select(9871), Draw(), Draw()] }, Path.Combine(directory, "gift-gacha"));
        var data = store.Read(1)!.Data;
        var saved = data.userGiftGachaWishes.Single();
        check(saved.giftGachaExchangeId == 9871 && saved.createdAt == createdAt && saved.updatedAt >= createdAt
            && data.userChargedCurrency.paid == 1500 && data.userGachas.Single().count == 3 && data.userGachaCeilItems.Single().quantity == 30,
            "现有场景执行器串联剩余次数改选和两次十连，创建时间、付费、次数与天井正确");
        check(data.userMaterials.Single().quantity == 11 && data.userPracticeTickets.Single().quantity == 4
            && data.userSkillPracticeTickets.Single().quantity == 6 && data.userBoostItems.Single().quantity == 8
            && data.userGamedata.coin == 110 && data.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 9870).quantity == 17
            && data.userMysekaiMaterials.Single(m => m.mysekaiMaterialId == 9871).quantity == 4 && data.userMysekaiMaterialPossession.quantity == 17,
            "三次抽取后六类赠品全入库存，角色材料不增加普通材料仓库占用");
        var expected = JsonNode.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(data)))!;
        foreach (var row in expected["userGachaCeilItems"]!.AsArray()) row!.AsObject().Remove("userId");
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            foreach (var key in new[] { "userGiftGachaWishes", "userGachas", "userGachaCeilItems", "userCards", "userMaterials",
                "userPracticeTickets", "userSkillPracticeTickets", "userBoostItems", "userMysekaiMaterials", "userMysekaiMaterialPossession", "userChargedCurrency" })
                check(JsonNode.DeepEquals(suite[key], expected[key]), "独立认证Suite持久回读赠品及相关状态：" + key);
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await fresh.Send(Select(9870)); }
            catch (ClientFailure) { }
            check(fresh.LastHttpStatus == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "名额耗尽后的选择修改真实HTTP拒绝，旧选择和全部资源不变");
        }
        foreach (var kind in new[] { "missing", "cross-pool", "partial" })
        {
            Reset();
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            if (kind == "partial") await invalid.Send(Select(9872));
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await invalid.Send(kind == "cross-pool" ? Select(9873) : Draw()); }
            catch (ClientFailure) { }
            check(invalid.LastHttpStatus == (kind == "missing" ? 409 : kind == "cross-pool" ? 400 : 500)
                && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "无选择、跨池选择及赠品部分发放失败的HTTP拒绝和整体回滚：" + kind);
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
