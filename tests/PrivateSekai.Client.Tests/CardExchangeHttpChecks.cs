using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class CardExchangeHttpChecks
{
    public static async Task Run(TargetConfiguration config, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userCards = [new() { userId = 1, cardId = 1, duplicateCount = 2, level = 7, totalExp = 55 }];
        state.Data.userMaterials = [];
        state.Data.refreshableTypes = [];
        store.Save(1, state);
        ScenarioStep Exchange(string body) => new() { Operation = "card-exchange", Body = JsonNode.Parse(body)!.AsObject() };
        var captures = Path.Combine(directory, "card-exchange-owner");
        using var client = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        client.CaptureTo(captures);
        await client.Send(new() { Operation = "system" });
        var step = Exchange("""{"userCards":[{"cardId":1,"duplicateCount":1}]}""");
        var result = await client.Send(step);
        check(result["updatedResources"]!["userCards"]![0]!["duplicateCount"]!.GetValue<int>() == 1 &&
            result["updatedResources"]!["userMaterials"]![0]!["quantity"]!.GetValue<int>() == 3 &&
            store.Read(1)!.Data.userCards.Single().level == 7 && store.Read(1)!.Data.userCards.Single().totalExp == 55,
            "候场室HTTP按剩余数扣除重复卡并发奖，不使用请求默认值覆盖养成");
        var request = Directory.EnumerateFiles(captures, "*_request.json").Select(path => JsonNode.Parse(File.ReadAllText(path))!)
            .Single(r => r["method"]!.GetValue<string>() == "PUT");
        check(request["body"]!["userCards"]![0]!["userId"]!.GetValue<string>() == "<redacted>" &&
            step.Body!["userCards"]![0]!["userId"] == null, "Client绑定当前账号且抓包脱敏，不改调用方卡牌详情");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        var requests = Directory.EnumerateFiles(captures, "*_request.json").Count();
        var blocked = false;
        try { await client.Send(Exchange("""{"userCards":[{"userId":2,"cardId":1,"duplicateCount":0}]}""")); }
        catch (InvalidOperationException) { blocked = true; }
        check(blocked && Directory.EnumerateFiles(captures, "*_request.json").Count() == requests &&
            DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "不同账号的卡牌详情在发包前拒绝");
        state = store.Read(1)!;
        state.Data.refreshableTypes = ["userMaterials"];
        store.Save(1, state);
        before = DumpSerializer.Serialize(state.Data);
        foreach (var entry in new[]
        {
            (Body: "{}", Status: 500, Validation: false),
            (Body: """{"userCards":[null]}""", Status: 500, Validation: false),
            (Body: """{"userCards":[{"cardId":2147483647,"duplicateCount":0}]}""", Status: 400, Validation: false),
            (Body: """{"userCards":[{"cardId":1,"duplicateCount":-1}]}""", Status: 400, Validation: true),
            (Body: """{"userCards":[{"cardId":1,"duplicateCount":1},{"cardId":1,"duplicateCount":1}]}""", Status: 404, Validation: false),
            (Body: """{"userCards":[{"cardId":1,"duplicateCount":1}]}""", Status: 409, Validation: false),
            (Body: """{"userCards":[{"cardId":1,"duplicateCount":2}]}""", Status: 409, Validation: false),
            (Body: """{"userCards":[{"cardId":1,"duplicateCount":0},{"cardId":2147483647,"duplicateCount":0}]}""", Status: 400, Validation: false)
        })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var rejected = false;
            try { await invalid.Send(Exchange(entry.Body)); } catch (ClientFailure) { rejected = true; }
            check(rejected && invalid.LastHttpStatus == entry.Status && (entry.Status == 500 ?
                invalid.LastResponse?["byteCount"]?.GetValue<int>() == 0 : entry.Validation ?
                invalid.LastResponse?["userCards[0].duplicateCount"]?.GetValue<string>() == "Min" :
                invalid.LastResponse?["errorCode"]?.GetValue<string>() == "" && invalid.LastResponse?["errorMessage"]?.GetValue<string>() == ""),
                "候场室HTTP按官方返回字段校验、空正文和普通错误形态");
            check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "候场室HTTP拒绝不扣发，不消费待刷新字段");
        }
        await client.Send(new() { Operation = "system" });
        var empty = await client.Send(Exchange("""{"userCards":[]}"""));
        check(empty["updatedResources"]!["userCards"] == null && store.Read(1)!.Data.userCards.Single().duplicateCount == 1 &&
            store.Read(1)!.Data.userMaterials.Single().quantity == 3, "空兑换成功且不刷新未变的卡牌或重复发奖");
        using (var readback = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await readback.Send(new() { Operation = "system" });
            var suite = await readback.Suite();
            check(suite["userCards"]![0]!["duplicateCount"]!.GetValue<int>() == 1 &&
                suite["userMaterials"]![0]!["quantity"]!.GetValue<int>() == 3,
                "独立登录回读同时保存重复卡扣除和材料到账");
        }
        store.Save(1, original);
    }
}
