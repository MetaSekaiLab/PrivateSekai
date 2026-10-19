extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class AvatarMotionFavoriteHttpChecks
{
    public static void WriteMaster(string directory)
    {
        var path = Path.Combine(directory, "configs.json");
        var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        rows.Add(JsonNode.Parse("""{"configKey":"favorite_avatar_motion_max_count","value":"5"}"""));
        JsonFiles.Write(path, rows);
    }

    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userAvatarMotions = Enumerable.Range(10, 5).Select(id => new UserAvatarMotion { avatarMotionId = id }).ToArray();
        state.Data.userAvatarMotionFavorites = [new() { avatarMotionId = 10, num = 0 }];
        store.Save(1, state);
        ScenarioStep Save(params (int Id, int Num)[] rows) => new()
        {
            Operation = "avatar-motion-favorite-save", Body = new JsonObject
            {
                ["userAvatarMotionFavorites"] = new JsonArray(rows.Select(row => (JsonNode?)new JsonObject
                    { ["avatarMotionId"] = row.Id, ["num"] = row.Num }).ToArray())
            }
        };
        var scenario = new Scenario { Steps = [Save((14, 0), (10, 1)), Save((10, 1), (14, 0)), Save((14, 4)),
            Save(), Save(), Save((11, 0)), new() { Operation = "avatar-motion-favorite-save", Body = new() }] };
        var output = Path.Combine(directory, "avatar-motion-favorite");
        await ScenarioRunner.Run(client, scenario, output);
        JsonNode Record(int index) => JsonNode.Parse(File.ReadAllText(Path.Combine(output, $"{index:D3}.json")))!;
        var first = Record(1)["response"]!["updatedResources"]!["userAvatarMotionFavorites"]!.AsArray();
        check(first[0]!["avatarMotionId"]!.GetValue<int>() == 10 && first[0]!["num"]!.GetValue<int>() == 1 &&
            first[1]!["avatarMotionId"]!.GetValue<int>() == 14 && first[1]!["num"]!.GetValue<int>() == 0,
            "动作快捷栏通过真实加密HTTP按完整列表替换，响应按动作编号排列");
        check(Record(2)["response"]!["updatedResources"]!["userAvatarMotionFavorites"]!.AsArray().Count == 2,
            "动作快捷栏重复非空保存仍返回全列表");
        check(Record(3)["after"]!["userAvatarMotionFavorites"]![0]!["num"]!.GetValue<int>() == 4,
            "动作快捷栏独立Suite保留指定末槽，不压缩空槽");
        check(Record(4)["response"]!["updatedResources"]!["userAvatarMotionFavorites"]!.AsArray().Count == 0 &&
            Record(5)["response"]!["updatedResources"]!["userAvatarMotionFavorites"] == null,
            "清空快捷栏返回空列表，重复清空省略刷新字段");
        check(Record(7)["after"]!["userAvatarMotionFavorites"]!.AsArray().Count == 0 &&
            store.Read(1)!.Data.userAvatarMotions.Length == 5, "缺失列表清空快捷栏并持久化，不改变持有动作");
        var bytes = Operations.EncodeBody(Operations.All["avatar-motion-favorite-save"], scenario.Steps[0].Body)!;
        check(DumpSerializer.Deserialize<UserAvatarMotionFavoriteRequest>(bytes).userAvatarMotionFavorites[0].num == 0 &&
            Operations.All["avatar-motion-favorite-save"].Method == "PATCH", "动作快捷栏使用dump契约保留零号槽位及PATCH方法");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var entry in new[] { (Step: Save((999, 0)), Status: 409), (Step: Save((10, 5)), Status: 409),
            (Step: Save((10, 0), (11, 0)), Status: 400), (Step: Save((999, 0), (999, 1)), Status: 409) })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(entry.Step); }
            catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == entry.Status && invalid.LastResponse?["httpStatus"]?.GetValue<int>() == entry.Status &&
                invalid.LastResponse?["errorCode"]?.GetValue<string>() == "" && invalid.LastResponse?["errorMessage"]?.GetValue<string>() == "",
                "动作快捷栏错误状态与空错误码响应通过真实HTTP，未持有优先于重复检查");
        }
        check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "快捷栏HTTP拒绝整体保留持久状态");
        await client.Send(new() { Operation = "system" });
        var rejected = false;
        try { ScenarioRunner.Validate(scenario, [new() { Kind = "official", BaseUrl = "https://example.invalid" }], new HashSet<string>()); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "动作快捷栏官方写操作需要显式启用");
        store.Save(1, original);
    }
}
