using System.Text.Json.Nodes;
using MessagePack;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class GachaFreebieHttpChecks
{
    public static void WriteMaster(string directory)
    {
        foreach (var (table, row) in new[]
        {
            ("gachas", """
                {"id":9860,"gachaType":"normal","startAt":1,"spinLimit":12,"gachaFreebieGroupId":9860,"gachaCeilItemId":9860,
                 "gachaDetails":[{"cardId":9864,"weight":1}],
                 "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
                 "gachaBehaviors":[{"id":9860,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":9860},
                  {"id":9861,"spinCount":10,"costResourceType":"jewel","costResourceQuantity":3000,"gachaExtraId":9860}]}
                """),
            ("gachas", """
                {"id":9861,"gachaType":"normal","startAt":1,"gachaFreebieGroupId":9861,
                 "gachaDetails":[{"cardId":9864,"weight":1}],
                 "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
                 "gachaBehaviors":[{"id":9860,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300}]}
                """),
            ("cards", """{"id":9864,"characterId":1,"cardRarityType":"rarity_2"}"""),
            ("gachaExtras", """{"id":9860,"resourceBoxId":9860}"""),
            ("gachaFreebieGroups", """{"id":9860,"groupId":9860,"seq":1,"rarity":3,"weight":1,"resourceBoxId":9860}"""),
            ("gachaFreebieGroups", """{"id":9861,"groupId":9861,"seq":1,"rarity":1,"weight":1,"resourceBoxId":9861}"""),
            ("resourceBoxes", """{"id":9860,"resourceBoxPurpose":"gacha_extra","resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":98,"resourceQuantity":5}]}"""),
            ("resourceBoxes", """
                {"id":9860,"resourceBoxPurpose":"gacha_freebie_group","resourceBoxType":"expand","details":[
                 {"resourceType":"gacha_ticket","resourceId":9860,"resourceQuantity":2},
                 {"resourceType":"material","resourceId":98,"resourceQuantity":3},
                 {"resourceType":"practice_ticket","resourceId":9860,"resourceQuantity":1},
                 {"resourceType":"skill_practice_ticket","resourceId":9860,"resourceQuantity":1},
                 {"resourceType":"boost_item","resourceId":9860,"resourceQuantity":1},
                 {"resourceType":"coin","resourceQuantity":4},{"resourceType":"virtual_coin","resourceQuantity":5}]}
                """),
            ("resourceBoxes", """
                {"id":9861,"resourceBoxPurpose":"gacha_freebie_group","resourceBoxType":"expand","details":[
                 {"resourceType":"material","resourceId":98,"resourceQuantity":1},
                 {"resourceType":"unknown_freebie","resourceId":1,"resourceQuantity":1}]}
                """)
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
            state.Data.userChargedCurrency = new() { free = 6000 };
            state.Data.userGamedata.coin = 100;
            state.Data.userGamedata.virtualCoin = 0;
            state.Data.userCards = [];
            state.Data.userGachas = [];
            state.Data.userMaterials = [];
            state.Data.userGachaTickets = [];
            state.Data.userGachaCeilItems = [];
            state.Data.userBoostItems = [];
            state.Data.userPracticeTickets = [];
            state.Data.userSkillPracticeTickets = [];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Draw(int behavior, int pool = 9860) => new() { Operation = "gacha-draw", Args = new()
            { ["gachaId"] = pool.ToString(), ["gachaBehaviorId"] = behavior.ToString() } };
        Reset();
        var ten = await client.Send(Draw(9861));
        var freebies = ten["obtainGachaFreebies"]!.AsArray();
        check(freebies.Count == 10 && freebies.All(f => f!["rarity"]!.GetValue<int>() == 3 && f["obtainedResources"]!.AsArray().Count == 7)
            && freebies[0]!["obtainedResources"]![0]!["quantity"]!.GetValue<int>() == 2
            && ten["obtainPrizes"]!.AsArray().Count == 10 && ten["obtainGachaExtras"]!.AsArray().Count == 0,
            "实际加密HTTP十连返回十个独立且顺序完整的附赠箱，每箱保留奖励量，不混入固定附赠或卡牌");
        check(ten["updatedResources"]!["userGachaTickets"]!.AsArray().Single()!["quantity"]!.GetValue<int>() == 20
            && ten["updatedResources"]!["userMaterials"]!.AsArray().Single()!["quantity"]!.GetValue<int>() == 35,
            "十连随机票券20、随机材料30及一次固定材料5通过刷新返回持有余额");
        var rest = Draw(9860);
        rest.Query["executeCount"] = "2";
        var two = await client.Send(rest);
        var data = store.Read(1)!.Data;
        check(two["obtainGachaFreebies"]!.AsArray().Count == 2 && data.userGachaTickets.Single().quantity == 24
            && data.userMaterials.Single().quantity == 51 && data.userPracticeTickets.Single().quantity == 12
            && data.userSkillPracticeTickets.Single().quantity == 12 && data.userBoostItems.Single().quantity == 12
            && data.userGamedata.coin == 148 && data.userGamedata.virtualCoin == 60,
            "指定剩余两抽发两箱随机奖励及两箱固定附赠，七类库存正确累计");
        check(data.userChargedCurrency.free == 2400 && data.userGachaCeilItems.Single().quantity == 12
            && data.userGachas.Single(g => g.gachaBehaviorId == 9861).count == 1
            && data.userGachas.Single(g => g.gachaBehaviorId == 9860).count == 2 && data.userCards.Single().duplicateCount == 11,
            "随机附赠保持既有费用、天井、行为次数和重复卡结算");
        var expected = JsonNode.Parse(MessagePackSerializer.ConvertToJson(DumpSerializer.Serialize(data)))!;
        foreach (var key in new[] { "userGachaTickets", "userGachaCeilItems" })
            foreach (var row in expected[key]!.AsArray()) row!.AsObject().Remove("userId");
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            foreach (var key in new[] { "userMaterials", "userGachaTickets", "userPracticeTickets", "userSkillPracticeTickets",
                "userBoostItems", "userGachaCeilItems", "userGachas", "userCards", "userChargedCurrency" })
                check(JsonNode.DeepEquals(suite[key], expected[key]), "独立认证Suite持久回读随机附赠及相关抽卡状态：" + key);
            check(suite["userGamedata"]!["coin"]!.GetValue<int>() == 148
                && suite["userGamedata"]!["virtualCoin"]!.GetValue<int>() == 60,
                "独立Suite保留两种附赠货币");
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await fresh.Send(Draw(9860)); }
            catch (ClientFailure) { }
            check(fresh.LastHttpStatus == 409 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "限量池耗尽的真实HTTP拒绝不再次扣款或发附赠");
        }
        await client.Send(new() { Operation = "system" });
        Reset();
        using (var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await invalid.Send(new() { Operation = "system" });
            var beforeFailure = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await invalid.Send(Draw(9860, 9861)); }
            catch (ClientFailure) { }
            check(invalid.LastHttpStatus == 500 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(beforeFailure),
                "随机箱部分资源发放后遇到未知资源，实际HTTP整体回滚费用、发卡、任务及部分材料");
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
