extern alias game;

using System.Text.Json;
using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class FavoriteReplay
{
    public static void ImportMaster(string source, string destination)
    {
        foreach (var table in new[] { "configs", "unitStoryEpisodeGroups", "eventStories" })
            File.Copy(Path.Combine(source, table + ".json"), Path.Combine(destination, table + ".json"), true);
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string captures, string output)
    {
        var records = Directory.GetFiles(captures, "*.json").Order(StringComparer.Ordinal)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject())
            .Where(record => record["operation"]?.GetValue<string>() is "favorite-set" or "favorite-delete").ToArray();
        if (records.Length == 0 || records.Any(record => record["status"]?.GetValue<string>() != "completed"))
            throw new InvalidOperationException("需要完整成功的官方收藏步骤记录。");
        var before = records[0]["before"]!;
        var state = store.Read(1)!;
        var member = DumpContract.For(typeof(SuiteUser)).Members.Single(m => (string)m.Key == "userStoryFavorites");
        member.Set(state.Data, JsonSerializer.Deserialize(before["userStoryFavorites"]!.ToJsonString(), member.Type, DumpJson.Options));
        store.Save(1, state);
        var steps = records.Select(record => new ScenarioStep
        {
            Operation = record["operation"]!.GetValue<string>(),
            Args = JsonSerializer.Deserialize<Dictionary<string, string>>(record["args"]!.ToJsonString())!,
            Body = record["request"]?.DeepClone().AsObject()
        }).ToList();
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = steps }, output);
        for (var i = 0; i < records.Length; i++)
        {
            var local = JsonNode.Parse(File.ReadAllText(Path.Combine(output, $"{i + 1:D3}.json")))!.AsObject();
            JsonFiles.Write(Path.Combine(output, $"{i + 1:D3}-full-compare.json"), ScenarioRunner.Compare(records[i], local));
            // 收藏比较与完整响应比较同时保留，其他夹具状态不能视作相同账号基线。
            foreach (var record in new[] { records[i], local })
            {
                foreach (var side in new[] { "before", "after" })
                    record[side] = SelectFavorites(record[side]);
                record["response"] = SelectFavorites(record["response"]?["updatedResources"]);
            }
            JsonFiles.Write(Path.Combine(output, $"{i + 1:D3}-favorites-compare.json"), ScenarioRunner.Compare(records[i], local));
        }
        Console.WriteLine($"已重放 {records.Length} 个官方收藏步骤，完整响应及收藏字段分别保留比较结果。");
    }

    private static JsonObject SelectFavorites(JsonNode? state) => new()
    {
        ["userStoryFavorites"] = state?["userStoryFavorites"]?.DeepClone()
    };
}
