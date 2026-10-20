using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using PrivateSekai.Config;
using PrivateSekai.Modules.Gacha;
using PrivateSekai.Protocol;
using PrivateSekai.Shared.Master;
using PrivateSekai.Shared.Users;
using PrivateSekai.Storage;

namespace PrivateSekai.Tests;

internal static class GachaExtraChecks
{
    public static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "gacha-extra-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Table(string name, string json) => File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        Table("gachas", """
            [{"id":1,"startAt":1,"gachaDetails":[{"cardId":4,"weight":1}],
              "gachaCardRarityRates":[{"cardRarityType":"rarity_2","lotteryType":"normal","rate":100}],
              "gachaBehaviors":[{"id":1,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":1},
                {"id":2,"spinCount":10,"costResourceType":"jewel","costResourceQuantity":3000,"gachaExtraId":2},
                {"id":3,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":3},
                {"id":4,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":999},
                {"id":5,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":4},
                {"id":6,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":5},
                {"id":7,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":6},
                {"id":8,"spinCount":1,"costResourceType":"jewel","costResourceQuantity":300,"gachaExtraId":7}]}]
            """);
        Table("cards", """[{"id":4,"characterId":1,"cardRarityType":"rarity_2"}]""");
        Table("gachaExtras", """[{"id":1,"resourceBoxId":1},{"id":2,"resourceBoxId":2},{"id":3,"resourceBoxId":3},{"id":4,"resourceBoxId":4},{"id":5,"resourceBoxId":5},{"id":6,"resourceBoxId":6},{"id":7,"resourceBoxId":7}]""");
        Table("resourceBoxes", """
            [{"resourceBoxPurpose":"gacha_extra","id":1,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":3}]},
             {"resourceBoxPurpose":"gacha_extra","id":2,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":30}]},
             {"resourceBoxPurpose":"gacha_extra","id":3,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":1},
              {"resourceType":"costume_3d","resourceId":10,"resourceQuantity":1},{"resourceType":"costume_3d","resourceId":11,"resourceQuantity":1},
              {"resourceType":"costume_3d","resourceId":12,"resourceQuantity":1},{"resourceType":"custom_profile_collection_resource","resourceId":7,"resourceQuantity":2}]},
             {"resourceBoxPurpose":"gacha_extra","id":4,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":1},{"resourceType":"unverified_resource","resourceId":1,"resourceQuantity":1}]},
             {"resourceBoxPurpose":"gacha_extra","id":5,"resourceBoxType":"costume_3d","details":[{"resourceType":"costume_3d","resourceId":11,"resourceQuantity":1},{"resourceType":"costume_3d","resourceId":999,"resourceQuantity":1}]},
             {"resourceBoxPurpose":"gacha_extra","id":6,"resourceBoxType":"expand","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":2147483647}]},
             {"resourceBoxPurpose":"gacha_extra","id":7,"resourceBoxType":"lottery","details":[{"resourceType":"material","resourceId":7,"resourceQuantity":1}]}]
            """);
        Table("costume3ds", """[{"id":10,"characterId":1,"partType":"head"},{"id":11,"characterId":1,"partType":"body"},{"id":12,"characterId":1,"partType":"hair"}]""");
        Table("characterMissionV2s", """[{"id":1,"characterId":1,"characterMissionType":"collect_costume_3d","parameterGroupId":1}]""");
        Table("characterMissionV2ParameterGroups", """[{"id":1,"seq":1,"requirement":1}]""");
        Table("honorMissions", """[{"id":1,"honorMissionType":"collect_costume_3d","requirement":1}]""");
        foreach (var table in new[] { "cardEpisodes", "cardCostume3ds", "costume3dShopItems", "releaseConditions", "characterArchiveVoices", "beginnerMissionV2s" }) Table(table, "[]");
        var store = new MemoryUserStore();
        using var provider = new ServiceCollection().AddPrivateSekai()
            .AddSingleton(new MasterData(new MasterCacheConfig { PinTables = [] }, directory))
            .AddSingleton<IUserStore>(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<UserOperation>();
        var gacha = scope.ServiceProvider.GetRequiredService<GachaService>();
        void Reset()
        {
            var state = TestUsers.Create(1);
            state.Data.userChargedCurrency = new() { free = 6000 };
            state.Data.userCostume3dStatuses = [new() { costume3dId = 10, status = "available", obtainedAt = 123 }];
            state.Data.userCustomProfileResources = [new() { customProfileResourceType = "material", customProfileResourceId = 7, quantity = 19 }];
            store.Save(1, state);
        }
        void Draw(int behavior, int? count = null) => operation.Execute(1, () => gacha.ExecuteGacha(1, behavior, false, count));
        int Material() => store.Read(1)!.Data.userMaterials.Single(m => m.materialId == 7).quantity;
        Reset();
        Draw(1, 2);
        Check.That(Material() == 6 && store.Read(1)!.Data.userChargedCurrency.free == 5400
            && store.Read(1)!.Data.userGachas.Single().count == 2, "指定两次单抽附赠两份资源箱，费用和次数仍按两次结算");
        Reset();
        Draw(2);
        Check.That(Material() == 30 && store.Read(1)!.Data.userCards.Single().duplicateCount == 9
            && store.Read(1)!.Data.userGachas.Single().count == 1, "十连附赠按行为资源箱发一次，不把已配置的数量再乘十");
        Reset();
        Draw(3);
        Draw(3);
        var data = store.Read(1)!.Data;
        Check.That(Material() == 2 && data.userCostume3dStatuses.Count(c => c.status == "available") == 3
            && data.userCostume3dStatuses.Single(c => c.costume3dId == 10).obtainedAt == 123,
            "重复获得附赠套装不新增条目或改已拥有时间，材料仍逐次入账");
        Check.That(data.userCharacterMissions.Single(m => m.characterMissionType == "collect_costume_3d").progress == 1
            && data.userHonorMissions.Single(m => m.honorMissionType == "collect_costume_3d").progress == 1,
            "首次附赠身体服装只计一次角色和称号收集，头饰及发型不重复计套数");
        Check.That(data.userCustomProfileResources.Single(r => r.customProfileResourceType == "collection").quantity == 4
            && data.userCustomProfileResources.Single(r => r.customProfileResourceType == "material").quantity == 19,
            "同编号的名片收藏和其他名片素材分别保存，附赠收藏累计数量");
        var before = DumpSerializer.Serialize(data);
        Check.Throws<MessagePackSerializationException>(() => operation.Execute(1, () =>
        {
            gacha.ExecuteGacha(1, 3, false);
            return new BrokenResponse();
        }), "附赠之后响应编码失败必须抛出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "编码失败整体回滚费用、卡牌、附赠和收集任务");
        foreach (var behavior in new[] { 4, 5, 6, 8 })
        {
            Reset();
            before = DumpSerializer.Serialize(store.Read(1)!.Data);
            var rejected = false;
            try { Draw(behavior); }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException) { rejected = true; }
            Check.That(rejected && DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before),
                "缺失附赠、未知资源、无效服装或错误箱类型不留下扣款、卡牌或部分奖励");
        }
        Reset();
        before = DumpSerializer.Serialize(store.Read(1)!.Data);
        Check.Throws<OverflowException>(() => Draw(7, 2), "多次执行时附赠数量相乘检查溢出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "附赠乘法溢出回滚已发生的扣款和发卡");
        Reset();
        var overflow = store.Read(1)!;
        overflow.Data.userCustomProfileResources = [new() { customProfileResourceType = "collection", customProfileResourceId = 7, quantity = int.MaxValue }];
        store.Save(1, overflow);
        before = DumpSerializer.Serialize(overflow.Data);
        Check.Throws<OverflowException>(() => Draw(3), "名片收藏库存加法检查溢出");
        Check.That(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "末项收藏溢出回滚前面的材料、服装、任务和卡牌");
        Reset();
        Draw(3);
        var saved = store.Read(1)!;
        var database = Path.Combine(directory, "extra.db");
        using (var sqlite = new SqliteUserStore(database)) sqlite.Save(1, saved);
        using (var reopened = new SqliteUserStore(database))
        {
            var restored = JsonSerializer.SerializeToNode(reopened.Read(1)!.Data, DumpJson.Options)!.AsObject();
            var expected = JsonSerializer.SerializeToNode(saved.Data, DumpJson.Options)!.AsObject();
            foreach (var key in new[] { "now", "refreshableTypes" })
            {
                restored.Remove(key);
                expected.Remove(key);
            }
            Check.That(JsonNode.DeepEquals(restored, expected), "SQLite重启保留全部持久字段，包括抽卡附赠、所有库存、服装和收集任务");
        }
    }

    [MessagePackObject(AllowPrivate = true)]
    public sealed class BrokenResponse
    {
        [Key(0)] public int Value => throw new InvalidOperationException("fixture-response");
    }
}
