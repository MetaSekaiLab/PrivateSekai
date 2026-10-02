extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class PresentReplay
{
    public static TimeProvider Clock(string capturePath)
    {
        var capture = JsonNode.Parse(File.ReadAllText(capturePath))!;
        return new ReceiptClock(capture["response"]!["receivedUserPresents"]![0]!["receivedAt"]!.GetValue<long>());
    }

    private sealed class ReceiptClock(long receivedAt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(receivedAt);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string capturePath, string output)
    {
        var capture = JsonNode.Parse(File.ReadAllText(capturePath))!.AsObject();
        if (capture["operation"]?.GetValue<string>() != "present-receive" || capture["status"]?.GetValue<string>() != "completed")
            throw new InvalidOperationException("需要已完成的官方邮箱领取记录。");
        var before = capture["before"]!;
        var presents = JsonSerializer.Deserialize<List<UserPresentData>>(before["userPresents"]!.ToJsonString(), DumpJson.Options)!;
        var body = capture["request"]!.DeepClone().AsObject();
        var ids = body["presentIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray();
        if (ids.Length == 0 || ids.Any(id => presents.Single(p => p.presentId == id).resourceType != "practice_ticket"))
            throw new InvalidOperationException("此重放只处理练习券礼物，其他资源需导入对应的完整初始状态。");
        var state = store.Read(1)!;
        state.Data.userPresents = presents;
        var tickets = before["userPracticeTickets"]!.DeepClone().AsArray();
        foreach (var ticket in tickets) ticket!["userId"] = 1;
        state.Data.userPracticeTickets = JsonSerializer.Deserialize<UserPracticeTicket[]>(tickets.ToJsonString(), DumpJson.Options)!;
        var refreshFields = new[] { "unreadUserTopics", "userBillingRefunds", "userGachaCeilExchanges", "userHomeBanners",
            "userInformations", "userMaterialExchanges", "userRankMatchResult", "userUnprocessedOrders", "userViewableAppeal" };
        foreach (var field in DumpContract.For(typeof(SuiteUser)).Members.Where(m => refreshFields.Contains((string)m.Key)))
            if (before[(string)field.Key] is { } initial)
            {
                var imported = initial.DeepClone();
                BindLocalUser(imported);
                field.Set(state.Data, JsonSerializer.Deserialize(imported.ToJsonString(), field.Type, DumpJson.Options));
            }
        store.Save(1, state);
        Directory.CreateDirectory(output);
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [new() { Operation = "present-receive", Body = body }, new() { Operation = "present-history" }] }, output);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "001.json")))!.AsObject();
        // 只比较此次导入的库存和邮箱；不把其余夹具字段当作相同基线。
        foreach (var record in new[] { capture, local })
            foreach (var side in new[] { "before", "after" })
                record[side] = new JsonObject
                {
                    ["userPresents"] = record[side]!["userPresents"]?.DeepClone(),
                    ["userPracticeTickets"] = record[side]!["userPracticeTickets"]?.DeepClone()
                };
        JsonFiles.Write(Path.Combine(output, "compare.json"), ScenarioRunner.Compare(capture, local));
        Console.WriteLine("练习券礼物已按官方初始库存完成本地 HTTP 重放；比较记录保存在指定私有目录。");
    }

    private static void BindLocalUser(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var field in obj.ToArray())
                if (field.Key == "userId") obj[field.Key] = 1;
                else BindLocalUser(field.Value);
        else if (node is JsonArray array)
            foreach (var item in array) BindLocalUser(item);
    }
}
