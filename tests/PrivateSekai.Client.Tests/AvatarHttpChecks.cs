extern alias game;

using System.Text.Json.Nodes;
using game::Sekai;
using PrivateSekai.Client;
using PrivateSekai.Config;
using PrivateSekai.Protocol;
using PrivateSekai.Storage;

internal static class AvatarHttpChecks
{
    public static async Task Run(ProtocolClient client, TargetConfiguration config, MemoryUserStore store,
        string directory, Action<bool, string> check)
    {
        var original = store.Read(1)!;
        var state = store.Read(1)!;
        state.Data.userAvatar = new() { avatarSkinColorId = 5 };
        state.Data.userAvatarCostumes = [new() { avatarCostumeId = 1 }, new() { avatarCostumeId = 2 }];
        state.Data.userAvatarAccessories = [new() { avatarAccessoryId = 3 }, new() { avatarAccessoryId = 4 }];
        state.Data.userAvatarSkinColors = [new() { avatarSkinColorId = 5 }, new() { avatarSkinColorId = 6 }];
        state.Data.userAvatarCoordinates = [new() { avatarCoordinateId = 7 }];
        store.Save(1, state);
        ScenarioStep Save(int? costume = null, int? accessory = null, int? color = 5, int? coordinate = null) => new()
        {
            Operation = "avatar-save", Body = new() { ["avatarCostumeId"] = costume, ["avatarAccessoryId"] = accessory,
                ["avatarSkinColorId"] = color, ["avatarCoordinateId"] = coordinate }
        };
        var scenario = new Scenario { Steps = [Save(1, 3), Save(1, 3), Save(2, 4, 6), Save(2, color: 6),
            Save(), Save(color: 6, coordinate: 7), Save(1, 3), Save(accessory: 3)] };
        var output = Path.Combine(directory, "avatar");
        await ScenarioRunner.Run(client, scenario, output);
        JsonNode Record(int index) => JsonNode.Parse(File.ReadAllText(Path.Combine(output, $"{index:D3}.json")))!;
        check(Record(1)["response"]!["updatedResources"]!["userAvatar"]!["avatarCostumeId"]!.GetValue<int>() == 1 &&
            Record(2)["response"]!["updatedResources"]!["userAvatar"]!["avatarAccessoryId"]!.GetValue<int>() == 3,
            "外观保存和相同外观重复保存通过加密HTTP均返回完整外观");
        check(Record(3)["after"]!["userAvatar"]!["avatarCostumeId"]!.GetValue<int>() == 2 &&
            Record(3)["after"]!["userAvatar"]!["avatarAccessoryId"]!.GetValue<int>() == 4 &&
            Record(3)["after"]!["userAvatar"]!["avatarSkinColorId"]!.GetValue<int>() == 6,
            "独立Suite回读保持替换后的服装、配件和肤色");
        check(Record(4)["after"]!["userAvatar"]!["avatarAccessoryId"] == null &&
            Record(5)["after"]!["userAvatar"]!.AsObject().Count == 1,
            "卸下配件及只保留肤色按完整请求替换，空穿戴不保留旧值");
        check(Record(6)["after"]!["userAvatar"]!["avatarCoordinateId"]!.GetValue<int>() == 7 &&
            Record(7)["after"]!["userAvatar"]!["avatarCoordinateId"] == null,
            "已持有套装可保存，切回服装和配件后清除套装");
        check(store.Read(1)!.Data.userAvatar.avatarCostumeId == null && store.Read(1)!.Data.userAvatar.avatarAccessoryId == 3,
            "支持单独佩戴配件并在Suite持久化");
        check(!scenario.Steps[0].Body!.ContainsKey("userId") && Operations.All["avatar-save"].Method == "PUT" &&
            Operations.Path(Operations.All["avatar-save"], scenario.Steps[0], 1) == "/api/user/1/avatar",
            "外观操作使用PUT，运行时填入账号且不修改共享场景");
        var encoded = Operations.EncodeBody(Operations.All["avatar-save"], scenario.Steps[3].Body)!;
        check(DumpSerializer.Deserialize<UserAvatarRequest>(encoded).avatarAccessoryId == null &&
            DumpSerializer.Deserialize<UserAvatarRequest>(encoded).avatarSkinColorId == 6,
            "外观直接使用dump可空字段，不把未穿戴编码成零编号");
        var before = DumpSerializer.Serialize(store.Read(1)!.Data);
        foreach (var entry in new[] { (Step: Save(999, 3), Status: 409), (Step: Save(1, 999), Status: 409),
            (Step: Save(1, 3, 999), Status: 409), (Step: Save(coordinate: 999), Status: 409),
            (Step: new ScenarioStep { Operation = "avatar-save", Body = new() }, Status: 500),
            (Step: Save(1, 3, null), Status: 500) })
        {
            using var invalid = new ProtocolClient(config, directory, ServerConfig.AesKey.ToArray(), ServerConfig.AesIv.ToArray());
            await invalid.Send(new() { Operation = "system" });
            var failed = false;
            try { await invalid.Send(entry.Step); } catch (ClientFailure) { failed = true; }
            check(failed && invalid.LastHttpStatus == entry.Status && (entry.Status == 500
                ? invalid.LastResponse?["byteCount"]?.GetValue<int>() == 0
                : invalid.LastResponse?["errorCode"]?.GetValue<string>() == "" && invalid.LastResponse?["errorMessage"]?.GetValue<string>() == ""),
                "外观HTTP拒绝返回已核验状态，缺失肤色返回零字节错误正文");
        }
        await client.Send(new() { Operation = "system" });
        var rejected = false;
        try { await client.Send(new() { Operation = "avatar-save", Body = new() { ["userId"] = 2, ["avatarSkinColorId"] = 5 } }); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && client.LastHttpStatus == null, "外观场景在发出HTTP前拒绝其他账号编号");
        check(DumpSerializer.Serialize(store.Read(1)!.Data).SequenceEqual(before), "所有外观拒绝不部分修改穿戴或其他资源");
        store.Save(1, original);
    }
}
