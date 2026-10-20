using System.Text.Json.Nodes;
using MessagePack;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class ItemReceivableGachaHttpChecks
{
    public static void WriteMaster(string directory)
    {
        void Add(string table, JsonNode row)
        {
            var path = Path.Combine(directory, table + ".json");
            var rows = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray() : new JsonArray();
            rows.Add(row);
            JsonFiles.Write(path, rows);
        }
        for (var i = 1; i <= 5; i++) Add("cards", new JsonObject { ["id"] = 99600 + i, ["characterId"] = 1,
            ["cardRarityType"] = i <= 3 ? "rarity_4" : i == 4 ? "rarity_2" : "rarity_3", ["cardSupplyId"] = i == 3 ? 3 : 1 });
        foreach (var id in new[] { 9960, 9961, 9962 })
        {
            var pool = JsonNode.Parse("""
                {"startAt":1,"gachaDetails":[],"gachaBehaviors":[],
                 "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":99.999},
                  {"cardRarityType":"rarity_3","lotteryType":"normal","rate":0.001}]}
                """)!;
            pool["id"] = id;
            pool["gachaCeilItemId"] = id;
            pool["gachaBonusItemReceivableRewardGroupId"] = id;
            for (var i = 1; i <= 5; i++) pool["gachaDetails"]!.AsArray().Add(new JsonObject
                { ["id"] = id * 100 + i, ["cardId"] = 99600 + i, ["weight"] = 1 });
            pool["gachaBehaviors"]!.AsArray().Add(new JsonObject { ["id"] = id * 10 + 1, ["spinCount"] = 1,
                ["costResourceType"] = "jewel", ["costResourceQuantity"] = 300 });
            pool["gachaBehaviors"]!.AsArray().Add(new JsonObject { ["id"] = id * 10 + 2, ["spinCount"] = 10,
                ["costResourceType"] = "paid_jewel", ["costResourceQuantity"] = 1500, ["gachaBehaviorType"] = "over_rarity_3_once" });
            pool["gachaBehaviors"]!.AsArray().Add(new JsonObject { ["id"] = id * 10 + 3, ["spinCount"] = 1,
                ["costResourceType"] = "gacha_ticket", ["costResourceId"] = 9960, ["costResourceQuantity"] = 1 });
            Add("gachas", pool);
        }
        foreach (var row in JsonNode.Parse("""
            [{"id":99601,"groupId":9960,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"item","resourceBoxId":9960},
             {"id":99602,"groupId":9960,"gachaBonusBorderPoint":50,"gachaBonusRewardType":"not_owned_random_rarity4_fixed","cardSupplyGroupId":9960},
             {"id":99603,"groupId":9960,"gachaBonusBorderPoint":100,"gachaBonusRewardType":"not_owned_random_rarity4_fixed","cardSupplyGroupId":9961},
             {"id":99604,"groupId":9960,"gachaBonusBorderPoint":150,"gachaBonusRewardType":"selectable_rarity4_fixed","cardSupplyGroupId":9960},
             {"id":99605,"groupId":9960,"gachaBonusBorderPoint":200,"gachaBonusRewardType":"selectable_rarity4_fixed","cardSupplyGroupId":9961},
             {"id":99606,"groupId":9961,"gachaBonusBorderPoint":50,"gachaBonusRewardType":"item","resourceBoxId":9961},
             {"id":99607,"groupId":9961,"gachaBonusBorderPoint":100,"gachaBonusRewardType":"item","resourceBoxId":9961},
             {"id":99608,"groupId":9962,"gachaBonusBorderPoint":10,"gachaBonusRewardType":"random_item","gachaBonusRewardItemGroupId":9962},
             {"id":99609,"groupId":9962,"gachaBonusBorderPoint":20,"gachaBonusRewardType":"selectable_item","gachaBonusRewardItemGroupId":9962}]
            """)!.AsArray()) Add("gachaBonusItemReceivableRewards", row!.DeepClone());
        Add("gachaBonusRewardItemGroups", JsonNode.Parse("""{"id":99711,"groupId":9962,"seq":1,"resourceBoxId":9960,"weight":3}""")!);
        Add("gachaBonusRewardItemGroups", JsonNode.Parse("""{"id":99712,"groupId":9962,"seq":2,"resourceBoxId":9961,"weight":0}""")!);
        Add("cardSupplyGroups", new JsonObject { ["id"] = 9960, ["groupId"] = 9960, ["cardSupplyId"] = 1 });
        Add("cardSupplyGroups", new JsonObject { ["id"] = 9961, ["groupId"] = 9961, ["cardSupplyId"] = 1 });
        Add("cardSupplyGroups", new JsonObject { ["id"] = 9962, ["groupId"] = 9961, ["cardSupplyId"] = 3 });
        Add("resourceBoxes", JsonNode.Parse("""{"id":9960,"resourceBoxPurpose":"gacha_bonus_item_receivable_reward","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":15,"resourceQuantity":50},{"resourceType":"material","resourceId":16,"resourceQuantity":3}]}""")!);
        Add("resourceBoxes", JsonNode.Parse("""{"id":9961,"resourceBoxPurpose":"gacha_bonus_item_receivable_reward","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":166,"resourceQuantity":1}]}""")!);
        Add("cardCostume3ds", JsonNode.Parse("""{"cardId":99602,"costume3dId":996021}""")!);
        Add("costume3ds", JsonNode.Parse("""{"id":996021,"characterId":1,"partType":"head"}""")!);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset(float point, int id = 9960, bool owned = false)
        {
            var state = original.DeepClone();
            state.Data.userChargedCurrency = new() { free = 10000, paid = 10000 };
            state.Data.userGachas = [];
            state.Data.userGachaCeilItems = [];
            state.Data.userGachaTickets = [new() { userId = 1, gachaTicketId = 9960, quantity = 10 }];
            state.Data.userMaterials = [];
            state.Data.userCards = owned ? [new() { userId = 1, cardId = 99601, level = 1, skillLevel = 1 }] : [];
            state.Data.userCostume3dStatuses = [new() { costume3dId = 996021, status = "forbidden" }];
            state.Data.userGachaBonusPoints = [new() { userId = 1, gachaId = id, gachaBonusPoint = point, totalGachaBonusPoint = point }];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Draw(int behavior = 1, int id = 9960, int? card = null, int? count = null)
        {
            var step = new ScenarioStep { Operation = "gacha-draw", Args = new() { ["gachaId"] = id.ToString(), ["gachaBehaviorId"] = (id * 10 + behavior).ToString() } };
            if (card.HasValue) step.Query["selectedCardId"] = card.Value.ToString();
            if (count.HasValue) step.Query["executeCount"] = count.Value.ToString();
            return step;
        }
        Reset(9.5f);
        var item = await client.Send(Draw());
        check(item["obtainGachaBonusItems"]!.AsArray().Count == 2 && store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 15).quantity == 50
            && store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 16).quantity == 3, "跨10点加密HTTP发整箱道具且返回obtainGachaBonusItems");
        var repeat = await client.Send(Draw());
        check(repeat["obtainGachaBonusItems"]!.AsArray().Count == 0 && store.Read(1)!.Data.userGachaBonusPoints.Single().gachaBonusPoint == 10.5f,
            "已越过门槛的下一抽不重复发道具，继续累计半点");
        Reset(49.5f, owned: true);
        var ten = await client.Send(Draw(2));
        var prizes = ten["obtainPrizes"]!.AsArray();
        check(prizes.Count == 10 && prizes[0]!["card"]!["resourceId"]!.GetValue<int>() == 99602
            && prizes[0]!["gachaLotteryType"]!.GetValue<string>() == "fixed_gacha_bonus" && prizes[0]!["costume3d"]!.AsArray().Count == 1
            && prizes.Skip(1).Any(p => p!["card"]!["resourceId"]!.GetValue<int>() == 99605),
            "未持有四星与原始服装实际获得，剩余十连仍单独保证三星");
        check(store.Read(1)!.Data.userChargedCurrency.paid == 8500 && store.Read(1)!.Data.userGachaBonusPoints.Single().gachaBonusPoint == 59.5f,
            "付费半价十连加十点并只扣一次配置费用");
        Reset(199.5f);
        var state = store.Read(1)!;
        state.Data.userGachaBonusPoints = [.. state.Data.userGachaBonusPoints, new() { userId = 1, gachaId = 9961, gachaBonusPoint = 99.5f, totalGachaBonusPoint = 99.5f }];
        store.Save(1, state);
        var selected = await client.Send(Draw(3, card: 99603, count: 2));
        check(selected["obtainPrizes"]![0]!["card"]!["resourceId"]!.GetValue<int>() == 99603
            && selected["obtainPrizes"]![0]!["gachaLotteryType"]!.GetValue<string>() == "fixed_gacha_bonus", "selectedCardId与executeCount同时经HTTP传递，200点实际发限定供给的所选卡");
        await ScenarioRunner.Run(client, new() { Steps = [Draw(3, 9961, count: 2)] }, Path.Combine(directory, "item-receivable-gacha"));
        var data = store.Read(1)!.Data;
        check(data.userGachaBonusPoints.All(p => p.gachaBonusPoint == .5f)
            && data.userGachaBonusPoints.Single(p => p.gachaId == 9960).totalGachaBonusPoint == 200.5f
            && data.userGachaBonusPoints.Single(p => p.gachaId == 9961).totalGachaBonusPoint == 100.5f
            && data.userGachaTickets.Single().quantity == 6 && data.userGachaCeilItems.Sum(i => i.quantity) == 4
            && data.userMaterials.Single().materialId == 166 && data.userMaterials.Single().quantity == 1,
            "100和200点循环独立回零并保留半点余量，跨池点数、道具、票券与天井完整入库");
        var expected = JsonNode.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(data)))!;
        foreach (var key in new[] { "userGachaCeilItems", "userGachaTickets" })
            foreach (var row in expected[key]!.AsArray()) row!.AsObject().Remove("userId");
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            foreach (var key in new[] { "userGachaBonusPoints", "userGachas", "userGachaCeilItems", "userGachaTickets", "userCards", "userChargedCurrency" })
                check(JsonNode.DeepEquals(suite[key], expected[key]), "独立认证Suite完整回读道具型保底状态：" + key);
            check(suite["userMaterials"]!.AsArray().Single()!["quantity"]!.GetValue<int>() == 1, "独立Suite回读实际保底道具库存");
        }
        await client.Send(new() { Operation = "system" });
        Reset(9.5f, 9962);
        var randomItem = await client.Send(Draw(id: 9962));
        check(randomItem["obtainGachaBonusItems"]!.AsArray().Count == 2
            && store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 15).quantity == 50,
            "随机道具门槛按选项权重选择完整资源箱");
        Reset(19.5f, 9962);
        var itemStep = Draw(3, 9962, count: 2);
        itemStep.Query["selectedItemId"] = "99712";
        var selectedItem = await client.Send(itemStep);
        check(selectedItem["obtainGachaBonusItems"]!.AsArray().Single()!["resourceId"]!.GetValue<int>() == 166
            && store.Read(1)!.Data.userMaterials.Single().quantity == 1
            && store.Read(1)!.Data.userGachaBonusPoints.Single().gachaBonusPoint == .5f,
            "selectedItemId使用选项ID并允许随机权重为零的候选，与executeCount共同传递并循环结转");
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            check(suite["userMaterials"]!.AsArray().Single()!["materialId"]!.GetValue<int>() == 166
                && suite["userGachaBonusPoints"]!.AsArray().Single()!["totalGachaBonusPoint"]!.GetValue<float>() == 20.5f,
                "道具选择结果与循环累计积分可由独立会话回读");
        }
        foreach (var kind in new[] { "missing", "wrong-supply", "detail-id", "unrelated", "wrong-item", "missing-item", "item-box-id", "item-both" })
        {
            var itemChoice = kind is "missing-item" or "item-box-id" or "item-both";
            Reset(itemChoice ? 19.5f : kind == "unrelated" ? 0 : 149.5f, itemChoice ? 9962 : 9960);
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var step = Draw(id: itemChoice ? 9962 : 9960,
                card: itemChoice || kind is "missing" or "wrong-item" ? null : kind == "detail-id" ? 996001 : kind == "wrong-supply" ? 99603 : 99601);
            if (kind == "wrong-item") step.Query["selectedItemId"] = "99601";
            if (kind == "item-box-id") step.Query["selectedItemId"] = "9961";
            if (kind == "item-both")
            {
                step.Query["selectedItemId"] = "99712";
                step.Query["selectedCardId"] = "99601";
            }
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await invalid.Send(step); }
            catch (ClientFailure) { }
            check(invalid.LastHttpStatus == (kind is "missing" or "missing-item" ? 409 : 400)
                && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "非法保底选择的HTTP拒绝与完整回滚：" + kind);
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
