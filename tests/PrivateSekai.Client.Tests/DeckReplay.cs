extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class DeckReplay
{
    public static async Task Run(ProtocolClient client, MemoryUserStore store, string captures, string output)
    {
        var records = Directory.GetFiles(captures, "*.json").Order(StringComparer.Ordinal)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject())
            .Where(record => record["operation"]?.GetValue<string>() == "deck-save").ToArray();
        if (records.Length == 0 || records.Any(record => record["status"]?.GetValue<string>() != "completed"))
            throw new InvalidOperationException("需要完整成功的官方编队步骤记录。");
        var initial = records[0]["before"]!.DeepClone();
        BindLocalUser(initial);
        var state = store.Read(1)!;
        foreach (var member in DumpContract.For(typeof(SuiteUser)).Members
            .Where(m => (string)m.Key is "userCards" or "userDecks" or "userGamedata"))
            member.Set(state.Data, JsonSerializer.Deserialize(initial[(string)member.Key]!.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        var steps = records.Select(record =>
        {
            var body = record["request"]!.DeepClone().AsObject();
            BindLocalUser(body);
            // 抓包已隐藏名称，重放使用固定短名称；不比较名称文本或审核规则。
            foreach (var update in body["userDeckUpdates"]!.AsArray())
                update!["userDeck"]!["name"] = "Replay";
            return new ScenarioStep { Operation = "deck-save", Body = body };
        }).ToList();
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = steps }, output);
        for (var i = 0; i < records.Length; i++)
        {
            var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, $"{i + 1:D3}.json")))!.AsObject();
            JsonFiles.Write(Path.Combine(output, $"{i + 1:D3}-full-compare.json"), ScenarioRunner.Compare(records[i], local));
            foreach (var record in new[] { records[i], local })
            {
                foreach (var side in new[] { "before", "after" }) record[side] = SelectDecks(record[side]);
                record["response"] = SelectDecks(record["response"]?["updatedResources"]);
            }
            JsonFiles.Write(Path.Combine(output, $"{i + 1:D3}-decks-compare.json"), ScenarioRunner.Compare(records[i], local));
        }
        Console.WriteLine($"已重放 {records.Length} 个官方编队步骤，完整响应及编队状态分别保留比较结果。");
    }

    private static JsonObject SelectDecks(JsonNode? state) => new()
    {
        ["userDecks"] = state?["userDecks"]?.DeepClone(),
        ["mainDeckId"] = state?["userGamedata"]?["deck"]?.DeepClone()
    };

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
