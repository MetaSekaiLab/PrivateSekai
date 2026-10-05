extern alias game;

using System.Text.Json.Nodes;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Storage;

internal static class MusicMyListHttpChecks
{
    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var state = store.Read(1)!;
        state.Data.userMusicMyList = [new() { listNo = 5, name = "Other", musicIds = [6] }];
        store.Save(1, state);
        var save = new ScenarioStep { Operation = "music-my-list-save", Args = new() { ["listNo"] = "1" },
            Body = JsonNode.Parse("""{"name":"fixture-my-list","musicIds":[3,1,1]}""")!.AsObject() };
        await ScenarioRunner.Run(client, new() { Steps = [save] }, Path.Combine(directory, "my-list"));
        var response = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "my-list/001.json")))!["response"]!.AsObject();
        check(response.ContainsKey("updateResources") && !response.ContainsKey("updatedResources") &&
            response["updateResources"]!["userMyLists"]!.AsArray().Count == 2,
            "My List HTTP 使用独立响应键并返回所有列表");
        check(store.Read(1)!.Data.userMusicMyList[0].musicIds.SequenceEqual(new[] { 1, 1, 3 }),
            "My List HTTP 排序并保留重复和未持有歌曲");
        check(!File.ReadAllText(Path.Combine(directory, "my-list/001.json")).Contains("fixture-my-list"),
            "My List 名称在记录中脱敏");
        using (var duplicate = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray()))
        {
            await duplicate.Send(new() { Operation = "system" });
            try { await duplicate.Send(save); }
            catch (ClientFailure) when (duplicate.LastHttpStatus == 400) { }
            check(duplicate.LastHttpStatus == 400 && duplicate.LastResponse?["httpStatus"]?.GetValue<int>() == 400 &&
                duplicate.LastResponse?["errorCode"]?.GetValue<string>() == "", "重复 My List 保存返回加密的 400 错误体");
        }
        var reset = new ScenarioStep { Operation = "music-my-list-reset", Args = new() { ["listNo"] = "1" } };
        await client.Send(new() { Operation = "system" });
        await ScenarioRunner.Run(client, new() { Steps = [reset] }, Path.Combine(directory, "my-list-reset"));
        var saved = store.Read(1)!.Data.userMusicMyList;
        check(saved[0].name == "fixture-my-list" && saved[0].musicIds.Length == 0 && saved[1].musicIds.Single() == 6,
            "My List HTTP 重置保留名称和其他列表");
        using var repeatedReset = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
        await repeatedReset.Send(new() { Operation = "system" });
        try { await repeatedReset.Send(reset); }
        catch (ClientFailure) when (repeatedReset.LastHttpStatus == 404) { }
        check(repeatedReset.LastHttpStatus == 404 && repeatedReset.LastResponse?["errorMessage"]?.GetValue<string>() == "",
            "重复重置返回加密的 404 错误体");
        await client.Send(new() { Operation = "system" });
    }
}
