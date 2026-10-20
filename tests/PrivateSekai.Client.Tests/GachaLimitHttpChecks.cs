using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class GachaLimitHttpChecks
{
    public static void WriteMaster(string directory)
    {
        var path = Path.Combine(directory, "gachas.json");
        var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        rows.Add(JsonNode.Parse("""
            {"id":9800,"startAt":1,"gachaDetails":[{"cardId":1,"weight":1}],
             "gachaCardRarityRates":[{"cardRarityType":"rarity_1","lotteryType":"normal","rate":100}],
             "gachaBehaviors":[{"id":9801,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"normal","costResourceType":"jewel","costResourceQuantity":100},
               {"id":9802,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"once_a_day","costResourceType":"paid_jewel","costResourceQuantity":100},
               {"id":9803,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"once_a_day","gachaSpinnableType":"colorful_pass","resourceCategory":"free_resource"}]}
            """));
        rows.Add(JsonNode.Parse("""
            {"id":9801,"startAt":1,"gachaDetails":[{"cardId":1,"weight":1}],
             "gachaCardRarityRates":[{"cardRarityType":"rarity_1","lotteryType":"normal","rate":100}],
             "gachaBehaviors":[{"id":9804,"spinCount":1,"executeLimit":1,"gachaBehaviorType":"once_a_day","gachaSpinnableType":"colorful_pass","resourceCategory":"free_resource"}]}
            """));
        rows.Add(JsonNode.Parse("""
            {"id":9802,"startAt":1,"spinLimit":3,"gachaDetails":[{"cardId":1,"weight":1}],
             "gachaCardRarityRates":[{"cardRarityType":"rarity_1","lotteryType":"normal","rate":100}],
             "gachaBehaviors":[{"id":9811,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":100},
               {"id":9812,"spinCount":10,"costResourceType":"jewel","costResourceQuantity":1000}]}
            """));
        JsonFiles.Write(path, rows);
        path = Path.Combine(directory, "configs.json");
        rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        if (!rows.Any(c => c!["configKey"]!.GetValue<string>() == "date_change_hour"))
            rows.Add(JsonNode.Parse("""{"configKey":"date_change_hour","value":"4"}"""));
        JsonFiles.Write(path, rows);
        JsonFiles.Write(Path.Combine(directory, "colorfulPassV2s.json"), JsonNode.Parse("""
            [{"id":3,"dailyPaidGachaSpinLimit":2,"dailyFreeGachaSpinLimit":1,"weeklyColorfulPassGachaSpinLimit":1}]
            """)!);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        state.Data.userCards = [new() { cardId = 1, duplicateCount = 0 }];
        state.Data.userChargedCurrency = new() { free = 200, paid = 200 };
        state.Data.userColorfulPassV2 = null;
        state.Data.userGachaFreeResources = [];
        state.Data.userGachas = [new() { userId = 1, gachaId = 9800, gachaBehaviorId = 9801, count = 1, lastSpinAt = now },
            new() { userId = 1, gachaId = 9800, gachaBehaviorId = 9802, count = 1, lastSpinAt = now - 2 * 86400000L }];
        store.Save(1, state);
        var suite = await client.Suite();
        check(suite["userGachas"]!.AsArray().Single(g => g!["gachaBehaviorId"]!.GetValue<int>() == 9802)!["count"]!.GetValue<int>() == 0 &&
            suite["userGachas"]!.AsArray().Single(g => g!["gachaBehaviorId"]!.GetValue<int>() == 9801)!["count"]!.GetValue<int>() == 1,
            "Suite加密HTTP恢复旧每日计数，保留普通一次性计数");
        ScenarioStep Draw(int behavior, int pool = 9800) => new() { Operation = "gacha-draw", Args = new()
            { ["gachaId"] = pool.ToString(System.Globalization.CultureInfo.InvariantCulture), ["gachaBehaviorId"] = behavior.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        async Task Reject(ScenarioStep step)
        {
            var saved = store.Read(1)!;
            saved.Data.refreshableTypes = ["userMaterials"];
            store.Save(1, saved);
            var before = DumpSerializer.Serialize(saved.Data);
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(step); } catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == 409 && invalid.LastResponse?["errorCode"]?.GetValue<string>() == "" &&
                DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "次数、资格或跨池免费名额拒绝通过加密HTTP返回，失败保留全部状态与待刷新字段");
            await client.Send(new() { Operation = "system" });
        }
        await Reject(Draw(9801));
        var paid = await client.Send(Draw(9802));
        check(paid["userGacha"]!["count"]!.GetValue<int>() == 1 && store.Read(1)!.Data.userChargedCurrency.paid == 100,
            "每日次数恢复后真实HTTP只扣一次费用并重新从一计数");
        await Reject(Draw(9802));
        await Reject(Draw(9803));
        state = store.Read(1)!;
        state.Data.userColorfulPassV2 = new() { colorfulPassId = 3, expiredAt = checked((ulong)(now + 86400000L)) };
        store.Save(1, state);
        var free = await client.Send(Draw(9803));
        check(free["consumedCosts"]!.AsArray().Count == 0 && free["obtainGachaCeilItems"]!.AsArray().Count == 0 &&
            free["updatedResources"]!["userGachaFreeResources"]![0]!["gachaBehaviorType"]!.GetValue<string>() == "once_a_day",
            "免费抽卡HTTP返回全局日次使用记录，不扣资源和发天井");
        await Reject(Draw(9804, 9801));
        using (var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await readback.Send(new() { Operation = "system" });
            suite = await readback.Suite();
            check(suite["userGachaFreeResources"]!.AsArray().Count == 1 && suite["userChargedCurrency"]!["free"]!.GetValue<int>() == 200 &&
                suite["userChargedCurrency"]!["paid"]!.GetValue<int>() == 100 && suite["userCards"]![0]!["duplicateCount"]!.GetValue<int>() == 2,
                "独立会话确认免费名额只用一次，两次有效抽卡均持久化，拒绝不产生第三张卡");
        }
        await client.Send(new() { Operation = "system" });
        state = store.Read(1)!;
        state.Data.userChargedCurrency = new() { free = 400 };
        state.Data.userCards = [new() { cardId = 1, duplicateCount = 0 }];
        store.Save(1, state);
        await client.Send(Draw(9811, 9802));
        await Reject(Draw(9812, 9802));
        var partial = Draw(9811, 9802);
        partial.Query["executeCount"] = "2";
        var result = await client.Send(partial);
        check(result["obtainPrizes"]!.AsArray().Count == 2 && result["userGacha"]!["count"]!.GetValue<int>() == 3 &&
            result["updatedResources"]!["userChargedCurrency"]!["free"]!.GetValue<int>() == 100 &&
            store.Read(1)!.Data.userCards.Single().duplicateCount == 3,
            "限量池真实HTTP拒绝整次十连越界，指定剩余两抽按两份费用与两次计数完成收尾");
        await Reject(Draw(9811, 9802));
        store.Save(1, original);
    }
}
