using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class CardExtraHttpChecks
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
        for (var index = 0; index < 4; index++)
        {
            var id = 9950 + index;
            var card = 99501 + index;
            var pool = JsonNode.Parse("""
                {"startAt":1,"gachaDetails":[],"gachaBehaviors":[],
                 "gachaCardRarityRates":[{"cardRarityType":"rarity_4","lotteryType":"normal","rate":100}]}
                """)!;
            pool["id"] = id;
            pool["gachaCeilItemId"] = id;
            if (index == 2) pool["gachaBonusId"] = 2;
            pool["gachaDetails"]!.AsArray().Add(new JsonObject { ["cardId"] = card, ["weight"] = 1, ["fixedBonusWeight"] = 1 });
            pool["gachaBehaviors"]!.AsArray().Add(new JsonObject { ["id"] = id * 10 + 1, ["spinCount"] = 1, ["costResourceType"] = "jewel", ["costResourceQuantity"] = 300 });
            pool["gachaBehaviors"]!.AsArray().Add(new JsonObject { ["id"] = id * 10 + 2, ["spinCount"] = 10, ["costResourceType"] = "paid_jewel", ["costResourceQuantity"] = 1500 });
            pool["gachaBehaviors"]!.AsArray().Add(new JsonObject { ["id"] = id * 10 + 3, ["spinCount"] = 1, ["costResourceType"] = "gacha_ticket", ["costResourceId"] = 9950, ["costResourceQuantity"] = 1 });
            Add("gachas", pool);
            Add("cards", new JsonObject { ["id"] = card, ["characterId"] = 1, ["cardRarityType"] = "rarity_4" });
            Add("cardExtras", new JsonObject { ["id"] = id, ["cardId"] = card, ["resourceBoxId"] = id });
        }
        foreach (var row in JsonNode.Parse("""
            [{"id":9950,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"avatar_accessory","resourceId":995081,"resourceQuantity":1}]},
             {"id":9951,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"custom_profile_collection_resource","resourceId":995821,"resourceQuantity":1},{"resourceType":"stamp","resourceId":995878,"resourceQuantity":1}]},
             {"id":9952,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"mysekai_fixture","resourceId":995837,"resourceQuantity":1}]},
             {"id":9953,"resourceBoxPurpose":"card_extra","resourceBoxType":"expand","details":[{"resourceType":"custom_profile_collection_resource","resourceId":995822,"resourceQuantity":1},{"resourceType":"mysekai_fixture","resourceId":999998,"resourceQuantity":1}]},
             {"id":99511,"resourceBoxPurpose":"gacha_ceil_exchange","details":[{"resourceType":"card","resourceId":99502,"resourceQuantity":1}]}]
            """)!.AsArray()) Add("resourceBoxes", row!.DeepClone());
        Add("stamps", JsonNode.Parse("""{"id":995878,"characterId1":1}""")!);
        Add("mysekaiFixtures", JsonNode.Parse("""{"id":995837,"mysekaiFixtureType":"normal"}""")!);
        Add("cardCostume3ds", JsonNode.Parse("""{"cardId":99502,"costume3dId":995021}""")!);
        Add("costume3ds", JsonNode.Parse("""{"id":995021,"characterId":1,"partType":"head"}""")!);
        Add("gachaCeilExchangeSummaries", JsonNode.Parse("""
            {"id":9951,"startAt":1,"gachaCeilExchanges":[{"id":9951,"startAt":1,"resourceBoxId":99511,
             "gachaCeilExchangeCost":{"resourceType":"gacha_ceil_item","gachaCeilItemId":9951,"quantity":1}}]}
            """)!);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        await client.Send(new() { Operation = "system" });
        var original = store.Read(1)!;
        void Reset()
        {
            var state = original.DeepClone();
            state.Data.userCards = [];
            state.Data.userAvatarAccessories = [];
            state.Data.userStamps = [];
            state.Data.userCustomProfileResources = [];
            state.Data.userMysekaiFixtures = [];
            state.Data.userCostume3dStatuses = [];
            state.Data.userChargedCurrency = new() { free = 10000, paid = 10000 };
            state.Data.userGachaCeilItems = [];
            state.Data.userGachaCeilExchanges = [];
            state.Data.userGachaTickets = [new() { gachaTicketId = 9950, quantity = 10 }];
            state.Data.userGachas = [];
            state.Data.userGachaBonusPoints = [new() { gachaId = 9952, gachaBonusPoint = 49.5f, totalGachaBonusPoint = 49.5f }];
            state.Data.userHonorMissions = [];
            state.Data.userCharacterMissions = [];
            state.Data.userCharacterMissionStatuses = [];
            state.Data.refreshableTypes = [];
            store.Save(1, state);
        }
        ScenarioStep Draw(int id, int behavior = 1, int? count = null)
        {
            var step = new ScenarioStep { Operation = "gacha-draw", Args = new() { ["gachaId"] = id.ToString(), ["gachaBehaviorId"] = (id * 10 + behavior).ToString() } };
            if (count.HasValue) step.Query["executeCount"] = count.Value.ToString();
            return step;
        }
        Reset();
        var first = await client.Send(Draw(9950));
        var repeat = await client.Send(Draw(9950));
        check(first["obtainPrizes"]![0]!["cardExtra"]!.AsArray().Single()!["resourceId"]!.GetValue<int>() == 995081
            && repeat["obtainPrizes"]![0]!["cardExtra"]!.AsArray().Count == 0 && store.Read(1)!.Data.userAvatarAccessories.Length == 1,
            "头像饰品随抽卡HTTP实际获得，重复卡无重复附赠");
        Reset();
        await ScenarioRunner.Run(client, new() { Steps = [Draw(9951), Draw(9951), Draw(9951, 2), Draw(9951, 3, 2)] }, Path.Combine(directory, "card-extra"));
        var data = store.Read(1)!.Data;
        check(data.userCustomProfileResources.Single().quantity == 14 && data.userStamps.Single().stampId == 995878
            && data.userCostume3dStatuses.Single().status == "available" && data.userCards.Single().duplicateCount == 13,
            "场景内单抽、重复、十连与指定票券多抽逐张增加素材，唯一贴图和服装仅一份");
        check(data.userChargedCurrency.free == 9400 && data.userChargedCurrency.paid == 8500 && data.userGachaTickets.Single().quantity == 8
            && data.userGachaCeilItems.Single().quantity == 14, "附赠不改变原消费、抽数与天井计算");
        Reset();
        var bonus = await client.Send(Draw(9952));
        var normal = await client.Send(Draw(9952));
        check(bonus["obtainPrizes"]![0]!["gachaLotteryType"]!.GetValue<string>() == "fixed_gacha_bonus"
            && bonus["obtainPrizes"]![0]!["cardExtra"]!.AsArray().Single()!["resourceId"]!.GetValue<int>() == 995837
            && normal["obtainPrizes"]![0]!["cardExtra"]!.AsArray().Count == 0 && store.Read(1)!.Data.userMysekaiFixtures.Single().quantity == 1,
            "固定保底卡经HTTP发联动家具，后续重复卡不重发");
        Reset();
        var state = store.Read(1)!;
        state.Data.userGachaCeilItems = [new() { gachaCeilItemId = 9951, quantity = 5 }];
        store.Save(1, state);
        ScenarioStep Exchange(int count) => new() { Operation = "gacha-exchange", Body = new()
            { ["gachaCeilExchangeRequest"] = new JsonObject { ["gachaExchangeId"] = 9951, ["exchangeCount"] = count } } };
        var exchanged = await client.Send(Exchange(3));
        check(exchanged["obtainUserResources"]!.AsArray().First()!["resourceType"]!.GetValue<string>() == "card"
            && exchanged["obtainUserResources"]!.AsArray().Single(r => r!["resourceType"]!.GetValue<string>() == "custom_profile_collection_resource")!["quantity"]!.GetValue<int>() == 3,
            "天井多次换卡经HTTP返回卡牌及完整附赠，素材按获得张数累计");
        check((await client.Send(Exchange(1)))["obtainUserResources"]!.AsArray().Count == 2, "重复换卡仅返回卡和可累计素材");
        await client.Send(Draw(9950));
        await client.Send(Draw(9952));
        using (var fresh = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await fresh.Send(new() { Operation = "system" });
            var suite = await fresh.Suite();
            check(suite["userCustomProfileResources"]!.AsArray().Single()!["quantity"]!.GetValue<int>() == 4
                && suite["userAvatarAccessories"]!.AsArray().Single()!["avatarAccessoryId"]!.GetValue<int>() == 995081
                && suite["userStamps"]!.AsArray().Single()!["stampId"]!.GetValue<int>() == 995878,
                "独立认证Suite回读累计素材及唯一饰品、贴图");
            var fixture = suite["userMysekaiFixtures"]!.AsArray().Single()!;
            check(fixture["mysekaiFixtureId"]!.GetValue<int>() == 995837 && fixture["quantity"]!.GetValue<int>() == 1
                && fixture["textureId"]!.GetValue<int>() == 1 && fixture["lastObtainedAt"]!.GetValue<long>() > 0,
                "独立Suite回读家具数量、默认纹理和获得时间");
            check(suite["userCards"]!.AsArray().Count == 3 && suite["userGachaCeilItems"]!.AsArray().Single(r => r!["gachaCeilItemId"]!.GetValue<int>() == 9951)!["quantity"]!.GetValue<int>() == 1,
                "独立Suite回读换卡和抽卡共享的真实卡牌及剩余天井库存");
        }
        Reset();
        using (var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await invalid.Send(new() { Operation = "system" });
            var before = DumpSerializer.Serialize(store.Read(1)!.Data);
            try { await invalid.Send(Draw(9953)); } catch (ClientFailure) { }
            check(invalid.LastHttpStatus == 500 && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "HTTP附赠后项缺家具定义，先发素材、卡牌、费用和状态整体回滚");
        }
        await client.Send(new() { Operation = "system" });
        store.Save(1, original);
    }
}
