using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Storage;

internal static class FavoriteHttpChecks
{
    public static void WriteMaster(string directory)
    {
        var path = Path.Combine(directory, "configs.json");
        var configs = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        configs.Add(JsonNode.Parse("""{"configKey":"story_favorite_count_limit","value":"10"}"""));
        JsonFiles.Write(path, configs);
        File.WriteAllText(Path.Combine(directory, "unitStoryEpisodeGroups.json"), """[{"id":1}]""");
        File.WriteAllText(Path.Combine(directory, "eventStories.json"), """[{"id":2}]""");
    }

    public static async Task Run(ProtocolClient client, MemoryUserStore store, string directory, Action<bool, string> check)
    {
        check(new[] { "favorite-set", "favorite-delete" }.All(operation =>
            Operations.EncodeBody(Operations.All[operation], new JsonObject())!.SequenceEqual(new byte[] { 0x80 })),
            "收藏保存与删除按原模型编码为空 MessagePack map");
        var state = store.Read(1)!;
        state.Data.userStoryFavorites = [];
        store.Save(1, state);
        var scenario = new Scenario { Steps =
        [
            Set("1", "unit_story", "1"), Set("1", "event_story", "2"), Set("10", "unit_story", "1"),
            new() { Operation = "favorite-delete", Args = new() { ["shareNo"] = "1" }, Body = new() }
        ] };
        ScenarioRunner.Validate(scenario, [new() { BaseUrl = "http://localhost" }], new HashSet<string>());
        var output = Path.Combine(directory, "favorites");
        await ScenarioRunner.Run(client, scenario, output);
        var replaced = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "002.json")))!;
        var slots = replaced["response"]!["updatedResources"]!["userStoryFavorites"]!.AsArray();
        check(!slots[0]!.AsObject().ContainsKey("comment"), "未写评论的收藏按官方响应省略 comment");
        check(slots.Count == 1 && slots[0]!["storyType"]!.GetValue<string>() == "event_story" &&
            slots[0]!["storyId"]!.GetValue<int>() == 2, "同一收藏槽位保存新剧情会替换旧剧情");
        check(replaced["stateChanges"]!.AsArray().Count > 0, "收藏槽位变更记录前后状态差异");
        var remaining = store.Read(1)!.Data.userStoryFavorites.Single();
        check(remaining.shareNo == 10 && remaining.storyType == "unit_story" && remaining.storyId == 1,
            "删除指定槽位保留另一个槽位，最大槽位可用");
        await ScenarioRunner.Run(client, new() { Steps =
        [new() { Operation = "favorite-delete", Args = new() { ["shareNo"] = "10" }, Body = new(),
            Expect = new() { ["/updatedResources/userStoryFavorites"] = new JsonArray() } }]
        }, Path.Combine(output, "clear"));
        check(store.Read(1)!.Data.userStoryFavorites.Length == 0, "删除最后一个收藏返回并保存空列表");
        await ScenarioRunner.Run(client, new() { Steps =
        [Set("10", "unit_story", "1"), Set("1", "unit_story", "1"),
         Set("10", "event_story", "2"), Set("10", "unit_story", "1")]
        }, Path.Combine(output, "move"));
        var moved = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "move/002.json")))!["response"]!["updatedResources"]!["userStoryFavorites"]!.AsArray();
        check(moved.Count == 1 && moved[0]!["shareNo"]!.GetValue<int>() == 1,
            "同一剧情保存到空槽位时移动收藏，不保留原槽位副本");
        var movedToOccupied = store.Read(1)!.Data.userStoryFavorites;
        check(movedToOccupied.Length == 1 && movedToOccupied[0].shareNo == 10 && movedToOccupied[0].storyType == "unit_story",
            "同一剧情移入已占用槽位时替换目标并移除原槽位，不交换剧情");
    }

    private static ScenarioStep Set(string slot, string type, string id) => new()
    {
        Operation = "favorite-set", Args = new() { ["shareNo"] = slot, ["storyType"] = type, ["storyId"] = id }, Body = new()
    };
}
